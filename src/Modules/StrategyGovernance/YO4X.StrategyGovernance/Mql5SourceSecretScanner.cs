using System.Text.RegularExpressions;

namespace YO4X.StrategyGovernance;

public sealed record Mql5SourceSecretFinding(
    string RelativePath,
    int Line,
    string RuleCode);

public sealed class Mql5SourceSecretException : IOException
{
    internal Mql5SourceSecretException(Mql5SourceSecretFinding finding)
        : base(CreateSafeMessage(finding))
    {
        RelativePath = finding.RelativePath;
        Line = finding.Line;
        RuleCode = finding.RuleCode;
    }

    public string RelativePath { get; }

    public int Line { get; }

    public string RuleCode { get; }

    private static string CreateSafeMessage(Mql5SourceSecretFinding finding)
    {
        string safePath = new(
            finding.RelativePath
                .Take(240)
                .Select(static character => char.IsControl(character) ? '?' : character)
                .ToArray());
        return $"MQL5 source rejected: high-confidence secret material was detected "
            + $"(rule {finding.RuleCode}, file '{safePath}', line {finding.Line}).";
    }
}

public static class Mql5SourceSecretScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly RegexOptions RuleOptions = RegexOptions.CultureInvariant
        | RegexOptions.ExplicitCapture;

    private static readonly SecretRule[] Rules =
    [
        CreateRule(
            "MQL5_SECRET_PRIVATE_KEY",
            "-----BEGIN[ \\t]+(?:RSA[ \\t]+|EC[ \\t]+|OPENSSH[ \\t]+|DSA[ \\t]+)?PRIVATE[ \\t]+KEY-----",
            RegexOptions.IgnoreCase),
        CreateRule(
            "MQL5_SECRET_TELEGRAM_BOT_TOKEN",
            "(?<![A-Za-z0-9])[0-9]{8,12}:[A-Za-z0-9_-]{30,50}(?![A-Za-z0-9_-])"),
        CreateRule(
            "MQL5_SECRET_AWS_ACCESS_KEY",
            "(?<![A-Z0-9])(?:AKIA|ASIA)[A-Z0-9]{16}(?![A-Z0-9])"),
        CreateRule(
            "MQL5_SECRET_GITHUB_TOKEN",
            "(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{82,255})(?![A-Za-z0-9_])"),
        CreateRule(
            "MQL5_SECRET_OPENAI_KEY",
            "(?<![A-Za-z0-9_-])sk-(?:(?:proj|svcacct)-)?[A-Za-z0-9_-]{20,}(?![A-Za-z0-9_-])"),
        CreateRule(
            "MQL5_SECRET_ANTHROPIC_KEY",
            "(?<![A-Za-z0-9_-])sk-ant-[A-Za-z0-9_-]{20,}(?![A-Za-z0-9_-])"),
        CreateRule(
            "MQL5_SECRET_GOOGLE_API_KEY",
            "(?<![A-Za-z0-9_-])AIza[A-Za-z0-9_-]{35}(?![A-Za-z0-9_-])"),
        CreateRule(
            "MQL5_SECRET_STRIPE_LIVE_KEY",
            "(?<![A-Za-z0-9_])(?:sk|rk)_live_[A-Za-z0-9]{16,}(?![A-Za-z0-9])"),
        CreateRule(
            "MQL5_SECRET_SLACK_TOKEN",
            "(?<![A-Za-z0-9_-])xox[baprs]-[A-Za-z0-9-]{20,}(?![A-Za-z0-9-])"),
        CreateRule(
            "MQL5_SECRET_JWT",
            "(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}(?![A-Za-z0-9_-])")
    ];

    private static readonly Regex SensitiveStringAssignment = new(
        "^[ \\t]*(?:(?:input|sinput)[ \\t]+)?(?:const[ \\t]+)?string[ \\t]+"
        + "(?<name>(?:telegram(?:bot)?token|telegramchatid|emailaddress|"
        + "(?:openai|anthropic|github|stripe|slack|aws)?_?api_?key|"
        + "(?:account|broker|database|db)?_?password|passphrase|client_?secret|access_?token))"
        + "[ \\t]*=[ \\t]*\"(?<value>(?:\\\\.|[^\"\\r\\n])*)\"",
        RuleOptions | RegexOptions.IgnoreCase | RegexOptions.Multiline,
        MatchTimeout);

    public static Mql5SourceSecretFinding? FindFirst(Mql5SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.RelativePath);
        ArgumentNullException.ThrowIfNull(document.Content);

        string source = Mql5SourceDecoder.Decode(document.Content).Text;
        Candidate? first = null;
        try
        {
            for (int ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
            {
                SecretRule rule = Rules[ruleIndex];
                Match match = rule.Pattern.Match(source);
                if (match.Success
                    && (first is null
                        || match.Index < first.Index
                        || match.Index == first.Index && ruleIndex < first.RuleIndex))
                {
                    first = new Candidate(match.Index, ruleIndex, rule.Code);
                }
            }

            int assignmentRuleIndex = Rules.Length;
            foreach (Match assignment in SensitiveStringAssignment.Matches(source))
            {
                string value = assignment.Groups["value"].Value;
                if (IsEmptyOrExplicitPlaceholder(value))
                {
                    continue;
                }

                string name = assignment.Groups["name"].Value;
                string code = name.Contains("email", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("chatid", StringComparison.OrdinalIgnoreCase)
                        ? "MQL5_EMBEDDED_NOTIFICATION_DESTINATION"
                        : "MQL5_EMBEDDED_NAMED_SECRET";
                int valueIndex = assignment.Groups["value"].Index;
                if (first is null
                    || valueIndex < first.Index
                    || valueIndex == first.Index && assignmentRuleIndex < first.RuleIndex)
                {
                    first = new Candidate(valueIndex, assignmentRuleIndex, code);
                }

                break;
            }
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new InvalidDataException(
                "MQL5 source rejected because the bounded secret scan did not complete.",
                exception);
        }

        return first is null
            ? null
            : new Mql5SourceSecretFinding(
                document.RelativePath,
                GetLine(source, first.Index),
                first.Code);
    }

    public static void EnsureNoHighConfidenceSecrets(Mql5SourceDocument document)
    {
        Mql5SourceSecretFinding? finding = FindFirst(document);
        if (finding is not null)
        {
            throw new Mql5SourceSecretException(finding);
        }
    }

    private static SecretRule CreateRule(
        string code,
        string pattern,
        RegexOptions additionalOptions = RegexOptions.None) => new(
            code,
            new Regex(pattern, RuleOptions | additionalOptions, MatchTimeout));

    private static bool IsEmptyOrExplicitPlaceholder(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if ((trimmed[0] == '<' && trimmed[^1] == '>')
            || (trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed[^1] == '}')
            || (trimmed[0] == '%' && trimmed[^1] == '%'))
        {
            return true;
        }

        string normalized = new(
            trimmed
                .Where(static character => char.IsLetterOrDigit(character))
                .Select(static character => char.ToLowerInvariant(character))
                .ToArray());
        if (normalized.Length == 0
            || normalized.All(static character => character is 'x' or '0'))
        {
            return true;
        }

        return normalized is "placeholder"
            or "changeme"
            or "replaceme"
            or "todo"
            or "none"
            or "null"
            or "demo"
            or "test"
            or "password"
            or "apikey"
            or "token"
            || normalized.StartsWith("your", StringComparison.Ordinal)
            || normalized.EndsWith("here", StringComparison.Ordinal);
    }

    private static int GetLine(string source, int index)
    {
        int line = 1;
        for (int position = 0; position < index; position++)
        {
            if (source[position] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private sealed record SecretRule(string Code, Regex Pattern);

    private sealed record Candidate(int Index, int RuleIndex, string Code);
}
