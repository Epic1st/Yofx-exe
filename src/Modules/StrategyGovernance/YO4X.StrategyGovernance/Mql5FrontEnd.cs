using System.Security.Cryptography;

namespace YO4X.StrategyGovernance;

/// <summary>
/// The stage a source document reached before it stopped.
/// </summary>
public enum Mql5FrontEndStage
{
    Decoded,
    Lexed,
    Parsed,
    Lowered
}

/// <summary>
/// The result of running one source document through decode → lex → parse → lower.
///
/// <paramref name="Module"/> is non-null only when <paramref name="Stage"/> is
/// <see cref="Mql5FrontEndStage.Lowered"/>. A document that stops earlier always
/// carries at least one error diagnostic explaining where and why.
/// </summary>
public sealed record Mql5FrontEndResult(
    string RelativePath,
    string SourceSha256,
    string EncodingName,
    Mql5FrontEndStage Stage,
    Mql5IrV2Module? Module,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics)
{
    public bool Succeeded => Stage == Mql5FrontEndStage.Lowered && Module is not null;
}

/// <summary>
/// The MQL5 language front end: the single supported entry point from raw source
/// bytes to lowered IR.
///
/// This performs no name, type or overload resolution, and no semantic validation
/// of trading behaviour. A successful result means the source was understood
/// structurally — never that it is executable, compilable, or safe to trade.
/// </summary>
public static class Mql5FrontEnd
{
    public static Mql5FrontEndResult Compile(Mql5SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string sha256 = Convert.ToHexString(SHA256.HashData(document.Content)).ToLowerInvariant();
        Mql5DecodedSource decoded = Mql5SourceDecoder.Decode(document.Content);

        if (decoded.ContentKind != Mql5SourceContentKind.Text)
        {
            string code = decoded.ContentKind == Mql5SourceContentKind.AllNul
                ? "MQL5_FRONTEND_ALL_NUL_SOURCE"
                : "MQL5_FRONTEND_BINARY_SOURCE";
            return new Mql5FrontEndResult(
                document.RelativePath,
                sha256,
                decoded.EncodingName,
                Mql5FrontEndStage.Decoded,
                Module: null,
                [
                    new Mql5RestrictedDiagnostic(
                        code,
                        Mql5RestrictedDiagnosticSeverity.Error,
                        "The document is not decodable MQL5 text and was not parsed.",
                        1,
                        1),
                ]);
        }

        Mql5ParseResult parsed = Mql5Parser.Parse(document.RelativePath, sha256, decoded.Text);
        if (!parsed.Succeeded || parsed.Unit is null)
        {
            return new Mql5FrontEndResult(
                document.RelativePath,
                sha256,
                decoded.EncodingName,
                Mql5FrontEndStage.Lexed,
                Module: null,
                parsed.Diagnostics);
        }

        Mql5LoweringResult lowered = Mql5Lowering.Lower(parsed.Unit);
        var diagnostics = new List<Mql5RestrictedDiagnostic>(
            parsed.Diagnostics.Count + lowered.Diagnostics.Count);
        diagnostics.AddRange(parsed.Diagnostics);
        diagnostics.AddRange(lowered.Diagnostics);

        return new Mql5FrontEndResult(
            document.RelativePath,
            sha256,
            decoded.EncodingName,
            lowered.Succeeded && lowered.Module is not null
                ? Mql5FrontEndStage.Lowered
                : Mql5FrontEndStage.Parsed,
            lowered.Module,
            diagnostics);
    }
}
