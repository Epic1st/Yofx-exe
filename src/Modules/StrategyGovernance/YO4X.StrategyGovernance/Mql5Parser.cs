using System.Text;

namespace YO4X.StrategyGovernance;

/// <summary>Outcome of one MQL5 parse.</summary>
public sealed record Mql5ParseResult(
    bool Succeeded,
    Mql5CompilationUnit? Unit,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics);

/// <summary>
/// Recursive-descent parser for the MQL5 language front end.
///
/// The parser is total: it never throws and never recurses without bound. Every
/// syntax error is recorded as a diagnostic and followed by a synchronisation step
/// so that a single malformed construct cannot discard the rest of a translation
/// unit. Meaning is not evaluated here — the parser records what the source says.
/// </summary>
public static class Mql5Parser
{
    public const string ParserVersion = "yo4x-mql5-parser.v1";

    private const int MaximumNestingDepth = 512;
    private const int MaximumDiagnostics = 500;

    private const string CodeUnexpectedToken = "MQL5_PARSE_UNEXPECTED_TOKEN";
    private const string CodeExpectedIdentifier = "MQL5_PARSE_EXPECTED_IDENTIFIER";
    private const string CodeExpectedType = "MQL5_PARSE_EXPECTED_TYPE";
    private const string CodeExpectedSemicolon = "MQL5_PARSE_EXPECTED_SEMICOLON";
    private const string CodeExpectedOpenParen = "MQL5_PARSE_EXPECTED_OPEN_PAREN";
    private const string CodeExpectedCloseParen = "MQL5_PARSE_EXPECTED_CLOSE_PAREN";
    private const string CodeExpectedOpenBrace = "MQL5_PARSE_EXPECTED_OPEN_BRACE";
    private const string CodeExpectedCloseBrace = "MQL5_PARSE_EXPECTED_CLOSE_BRACE";
    private const string CodeExpectedCloseBracket = "MQL5_PARSE_EXPECTED_CLOSE_BRACKET";
    private const string CodeExpectedColon = "MQL5_PARSE_EXPECTED_COLON";
    private const string CodeExpectedExpression = "MQL5_PARSE_EXPECTED_EXPRESSION";
    private const string CodeExpectedWhile = "MQL5_PARSE_EXPECTED_WHILE";
    private const string CodeExpectedDeclaration = "MQL5_PARSE_EXPECTED_DECLARATION";
    private const string CodeExpectedTemplateParameter = "MQL5_PARSE_EXPECTED_TEMPLATE_PARAMETER";
    private const string CodeInvalidInclude = "MQL5_PARSE_INVALID_INCLUDE";
    private const string CodeInvalidDefine = "MQL5_PARSE_INVALID_DEFINE";
    private const string CodeInvalidProperty = "MQL5_PARSE_INVALID_PROPERTY";
    private const string CodeInvalidImport = "MQL5_PARSE_INVALID_IMPORT";
    private const string CodeUnterminatedImport = "MQL5_PARSE_UNTERMINATED_IMPORT";
    private const string CodeUnexpectedEnd = "MQL5_PARSE_UNEXPECTED_END";
    private const string CodeNestingLimit = "MQL5_PARSE_NESTING_LIMIT";
    private const string CodeDiagnosticLimit = "MQL5_PARSE_DIAGNOSTIC_LIMIT";
    private const string CodeInternalError = "MQL5_PARSE_INTERNAL_ERROR";
    private const string CodeNoTokens = "MQL5_PARSE_NO_TOKENS";
    private const string CodeLexFailed = "MQL5_PARSE_LEX_FAILED";

    /// <summary>Lexes and parses one translation unit.</summary>
    public static Mql5ParseResult Parse(string relativePath, string sourceSha256, string source)
    {
        Mql5LexResult lexed;
        try
        {
            lexed = Mql5Lexer.Tokenize(source ?? string.Empty);
        }
        catch (Exception)
        {
            return new(
                false,
                new(relativePath ?? string.Empty, sourceSha256 ?? string.Empty, []),
                [new(CodeLexFailed, Mql5RestrictedDiagnosticSeverity.Error, "The lexer failed on this source.", 1, 1)]);
        }

        return ParseTokens(relativePath, sourceSha256, lexed);
    }

    /// <summary>Parses a token stream produced by <see cref="Mql5Lexer"/>.</summary>
    public static Mql5ParseResult ParseTokens(string relativePath, string sourceSha256, Mql5LexResult lexed)
    {
        string path = relativePath ?? string.Empty;
        string hash = sourceSha256 ?? string.Empty;
        if (lexed is null || lexed.Tokens is null)
        {
            return new(
                false,
                new(path, hash, []),
                [new(CodeNoTokens, Mql5RestrictedDiagnosticSeverity.Error, "No token stream was supplied.", 1, 1)]);
        }

        List<Mql5RestrictedDiagnostic> diagnostics = [];
        if (lexed.Diagnostics is not null)
        {
            foreach (Mql5RestrictedDiagnostic diagnostic in lexed.Diagnostics)
            {
                if (diagnostic is not null && diagnostics.Count < MaximumDiagnostics)
                {
                    diagnostics.Add(diagnostic);
                }
            }
        }

        var walker = new Walker(ExpandAliasMacros(lexed.Tokens), diagnostics);
        IReadOnlyList<Mql5Declaration> declarations;
        try
        {
            declarations = walker.ParseCompilationUnit();
        }
        catch (Exception)
        {
            declarations = [];
            if (diagnostics.Count < MaximumDiagnostics)
            {
                diagnostics.Add(new(
                    CodeInternalError,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    "The parser aborted on an internal error.",
                    1,
                    1));
            }
        }

        bool succeeded = true;
        foreach (Mql5RestrictedDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error)
            {
                succeeded = false;
                break;
            }
        }

        return new(succeeded, new(path, hash, declarations), diagnostics);
    }

    /// <summary>
    /// Rewrites every identifier that an object-like <c>#define</c> aliases to another
    /// single identifier, honouring the order the directives appear in.
    /// </summary>
    /// <remarks>
    /// The parser otherwise records a <c>#define</c> and moves on, which is the right
    /// treatment for a macro that stands for a value: the replacement text survives into
    /// the IR and a binder classifies it there. It is the wrong treatment for the one
    /// shape the conversion corpus uses to fake the MQL4 dialect. Three files declare
    /// <c>double MQL4_iFractals(string,int,int,int)</c>, write
    /// <c>#define iFractals MQL4_iFractals</c> underneath it, and then call
    /// <c>iFractals(sym,0,1,1)</c>. Leaving the name alone sends a four-argument call to
    /// the two-parameter built-in, where it is refused as MQL4 arity — a diagnostic that
    /// is simply false, since MetaEditor compiles all three files with no errors. The
    /// same three files alias <c>iMA</c>, <c>iTime</c>, <c>OrderSend</c>,
    /// <c>OrderSelect</c>, <c>OrdersTotal</c>, <c>Point</c> and <c>Digits</c> the same way.
    ///
    /// Only the alias shape is expanded, and the lexer itself decides what qualifies: the
    /// replacement is re-tokenized and accepted only when it is exactly one identifier, so
    /// a macro standing for a literal, an expression, a reserved word, a function-like
    /// macro or nothing at all is left untouched. Directive order matters and is honoured,
    /// because C and MQL5 both make a macro visible only below its directive — that is
    /// precisely what lets the shim above the <c>#define</c> call the real built-in.
    /// <c>#undef</c> and redefinition drop the alias again. A chain of aliases resolves by
    /// re-applying the map, bounded by the number of live aliases and refusing to revisit a
    /// name, so a macro that names itself expands once and stops.
    /// </remarks>
    private static IReadOnlyList<Mql5Token> ExpandAliasMacros(IReadOnlyList<Mql5Token> tokens)
    {
        Dictionary<string, string>? aliases = null;
        List<Mql5Token>? rewritten = null;

        for (int index = 0; index < tokens.Count; index++)
        {
            Mql5Token? token = tokens[index];
            if (token is null)
            {
                continue;
            }

            if (token.Kind == Mql5TokenKind.PreprocessorDirective)
            {
                ReadAliasDirective(token, ref aliases);
                continue;
            }

            if (aliases is null || aliases.Count == 0 || token.Kind != Mql5TokenKind.Identifier)
            {
                continue;
            }

            string replacement = ResolveAlias(aliases, token.Text);
            if (string.Equals(replacement, token.Text, StringComparison.Ordinal))
            {
                continue;
            }

            rewritten ??= [.. tokens];
            rewritten[index] = token with { Text = replacement };
        }

        return rewritten ?? tokens;
    }

    /// <summary>
    /// Reads one preprocessor line for its effect on the alias map. Anything that is not
    /// an object-like <c>#define</c> naming a single identifier, or an <c>#undef</c>, only
    /// ever removes an alias — never adds a shape we did not confirm.
    /// </summary>
    private static void ReadAliasDirective(Mql5Token directive, ref Dictionary<string, string>? aliases)
    {
        string line = StripTrailingComment(directive.Value ?? directive.Text).Trim();
        if (!line.StartsWith('#'))
        {
            return;
        }

        string body = line[1..].TrimStart();
        int cut = 0;
        while (cut < body.Length && !char.IsWhiteSpace(body[cut]))
        {
            cut++;
        }

        string keyword = body[..cut];
        string payload = body[cut..].TrimStart();
        if (string.Equals(keyword, "undef", StringComparison.Ordinal))
        {
            if (aliases is not null && TryReadSingleIdentifier(payload, out string undefined))
            {
                aliases.Remove(undefined);
            }

            return;
        }

        if (!string.Equals(keyword, "define", StringComparison.Ordinal))
        {
            return;
        }

        int nameEnd = 0;
        while (nameEnd < payload.Length && (char.IsLetterOrDigit(payload[nameEnd]) || payload[nameEnd] == '_'))
        {
            nameEnd++;
        }

        if (nameEnd == 0)
        {
            return;
        }

        string name = payload[..nameEnd];

        // A '(' with no gap after the name is MQL5's function-like macro. Its parameter
        // list is not a replacement identifier, and it rebinds the name either way.
        string tail = payload[nameEnd..];
        if (tail.StartsWith('(')
            || !TryReadSingleIdentifier(tail, out string replacement)
            || string.Equals(replacement, name, StringComparison.Ordinal))
        {
            aliases?.Remove(name);
            return;
        }

        aliases ??= new Dictionary<string, string>(StringComparer.Ordinal);
        aliases[name] = replacement;
    }

    /// <summary>
    /// True when <paramref name="text"/> lexes to exactly one identifier and nothing else.
    /// The lexer is asked rather than a private copy of its rules, so a reserved word, a
    /// literal or a two-token replacement is rejected for the same reasons it would be
    /// rejected anywhere else in the front end.
    /// </summary>
    private static bool TryReadSingleIdentifier(string text, out string identifier)
    {
        identifier = string.Empty;
        string candidate = text.Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        Mql5LexResult lexed;
        try
        {
            lexed = Mql5Lexer.Tokenize(candidate);
        }
        catch (Exception)
        {
            return false;
        }

        if (lexed.Diagnostics.Count != 0
            || lexed.Tokens.Count != 2
            || lexed.Tokens[0].Kind != Mql5TokenKind.Identifier
            || lexed.Tokens[1].Kind != Mql5TokenKind.EndOfFile)
        {
            return false;
        }

        identifier = lexed.Tokens[0].Text;
        return true;
    }

    /// <summary>Follows a chain of aliases, stopping at the first name already visited.</summary>
    private static string ResolveAlias(Dictionary<string, string> aliases, string name)
    {
        string current = name;
        HashSet<string>? seen = null;
        for (int step = 0; step < aliases.Count; step++)
        {
            if (!aliases.TryGetValue(current, out string? next))
            {
                break;
            }

            seen ??= new HashSet<string>(StringComparer.Ordinal) { current };
            if (!seen.Add(next))
            {
                break;
            }

            current = next;
        }

        return current;
    }

    /// <summary>Removes a trailing line or block comment from a directive line.</summary>
    private static string StripTrailingComment(string line)
    {
        bool inText = false;
        bool inCharacter = false;
        for (int cursor = 0; cursor < line.Length; cursor++)
        {
            char current = line[cursor];
            if (current == '\\' && (inText || inCharacter))
            {
                cursor++;
                continue;
            }

            if (current == '"' && !inCharacter)
            {
                inText = !inText;
                continue;
            }

            if (current == '\'' && !inText)
            {
                inCharacter = !inCharacter;
                continue;
            }

            if (!inText && !inCharacter && current == '/' && cursor + 1 < line.Length
                && (line[cursor + 1] == '/' || line[cursor + 1] == '*'))
            {
                return line[..cursor].TrimEnd();
            }
        }

        return line;
    }

    /// <summary>Words that are never the beginning of a written type.</summary>
    private static readonly HashSet<string> NonTypeWords = new(
        [
            "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue",
            "return", "delete", "new", "sizeof", "operator", "template", "typename", "typedef",
            "struct", "class", "interface", "union", "enum", "input", "sinput", "extern", "static",
            "const", "virtual", "public", "protected", "private", "override", "final", "this",
            "true", "false", "dynamic_cast", "goto", "NULL", "export"
        ],
        StringComparer.Ordinal);

    /// <summary>Built-in type words; a parenthesised one of these is always a cast.</summary>
    private static readonly HashSet<string> BuiltInTypeWords = new(
        [
            "void", "bool", "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "string", "datetime", "color"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> AssignmentOperators = new(
        ["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>="],
        StringComparer.Ordinal);

    private static readonly HashSet<string> PrefixOperators = new(
        ["!", "~", "-", "+", "++", "--", "&", "*"],
        StringComparer.Ordinal);

    private sealed class Walker
    {
        private readonly List<Mql5Token> tokens;
        private readonly List<Mql5RestrictedDiagnostic> diagnostics;
        private readonly Queue<Mql5Declaration> pending = [];
        private int index;
        private int depth;
        private bool aborted;
        private bool limitReported;

        /// <summary>
        /// The caption of the most recent <c>input group "…"</c> marker, which applies to
        /// every declaration that follows it until the next marker. Null before the first
        /// marker and after a marker with an empty caption.
        /// </summary>
        private string? currentInputGroup;

        public Walker(IReadOnlyList<Mql5Token> source, List<Mql5RestrictedDiagnostic> sink)
        {
            diagnostics = sink;
            tokens = new List<Mql5Token>(source.Count + 1);

            // Conditional compilation is resolved by taking the first branch: everything
            // between '#else'/'#elif' and the matching '#endif' is dropped. Without this
            // the two branches of an '#ifdef' would be read as one contradictory sequence.
            int conditionalDepth = 0;
            int skippingFrom = 0;
            foreach (Mql5Token token in source)
            {
                if (token is null || token.Kind == Mql5TokenKind.EndOfFile)
                {
                    continue;
                }

                if (token.Kind == Mql5TokenKind.PreprocessorDirective)
                {
                    switch (DirectiveName(token))
                    {
                        case "if":
                        case "ifdef":
                        case "ifndef":
                            conditionalDepth++;
                            break;
                        case "else":
                        case "elif":
                            if (skippingFrom == 0 && conditionalDepth > 0)
                            {
                                skippingFrom = conditionalDepth;
                            }

                            break;
                        case "endif":
                            if (skippingFrom == conditionalDepth)
                            {
                                skippingFrom = 0;
                            }

                            if (conditionalDepth > 0)
                            {
                                conditionalDepth--;
                            }

                            break;
                        default:
                            break;
                    }
                }

                if (skippingFrom != 0)
                {
                    continue;
                }

                tokens.Add(token);
            }

            Mql5Token last = tokens.Count == 0 ? new(Mql5TokenKind.EndOfFile, string.Empty, null, 1, 1, 0) : tokens[^1];
            tokens.Add(new(Mql5TokenKind.EndOfFile, string.Empty, null, last.Line, last.Column, last.Position));
        }

        // ------------------------------------------------------------- plumbing

        /// <summary>The directive word of a preprocessor line, without its '#'.</summary>
        private static string DirectiveName(Mql5Token token)
        {
            string text = (token.Value ?? token.Text).TrimStart();
            if (!text.StartsWith('#'))
            {
                return string.Empty;
            }

            text = text[1..].TrimStart();
            int cut = 0;
            while (cut < text.Length && (char.IsLetterOrDigit(text[cut]) || text[cut] == '_'))
            {
                cut++;
            }

            return text[..cut];
        }

        /// <summary>
        /// Trims a caption taken from source and maps a blank one to null, so an absent
        /// group is reported as absent rather than as an empty string.
        /// </summary>
        private static string? NormalizeLabel(string? caption)
        {
            string trimmed = (caption ?? string.Empty).Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private Mql5Token Current => tokens[index];

        private Mql5Token Ahead(int offset)
        {
            int target = index + offset;
            return target < tokens.Count ? tokens[target] : tokens[^1];
        }

        private bool AtEnd => Current.Kind == Mql5TokenKind.EndOfFile;

        private void Advance()
        {
            if (index < tokens.Count - 1)
            {
                index++;
            }
        }

        private static bool IsSymbolKind(Mql5TokenKind kind) =>
            kind is Mql5TokenKind.Operator or Mql5TokenKind.Punctuator;

        private static bool IsWordKind(Mql5TokenKind kind) =>
            kind is Mql5TokenKind.Identifier or Mql5TokenKind.Keyword;

        private bool AtSymbol(string text) =>
            IsSymbolKind(Current.Kind) && string.Equals(Current.Text, text, StringComparison.Ordinal);

        private bool AtSymbol(int offset, string text)
        {
            Mql5Token token = Ahead(offset);
            return IsSymbolKind(token.Kind) && string.Equals(token.Text, text, StringComparison.Ordinal);
        }

        private bool AtWord(string text) =>
            IsWordKind(Current.Kind) && string.Equals(Current.Text, text, StringComparison.Ordinal);

        private bool AtWord(int offset, string text)
        {
            Mql5Token token = Ahead(offset);
            return IsWordKind(token.Kind) && string.Equals(token.Text, text, StringComparison.Ordinal);
        }

        private bool AtName() => IsWordKind(Current.Kind) && !NonTypeWords.Contains(Current.Text);

        private bool AtName(int offset)
        {
            Mql5Token token = Ahead(offset);
            return IsWordKind(token.Kind) && !NonTypeWords.Contains(token.Text);
        }

        private bool TakeSymbol(string text)
        {
            if (!AtSymbol(text))
            {
                return false;
            }

            Advance();
            return true;
        }

        private bool TakeWord(string text)
        {
            if (!AtWord(text))
            {
                return false;
            }

            Advance();
            return true;
        }

        private bool Expect(string text, string code)
        {
            if (TakeSymbol(text))
            {
                return true;
            }

            Report(code, "Expected '" + text + "'.", Current);
            return false;
        }

        private void Report(string code, string message, Mql5Token token)
        {
            if (diagnostics.Count < MaximumDiagnostics)
            {
                diagnostics.Add(new(code, Mql5RestrictedDiagnosticSeverity.Error, message, token.Line, token.Column));
                return;
            }

            if (!limitReported)
            {
                limitReported = true;
                diagnostics.Add(new(
                    CodeDiagnosticLimit,
                    Mql5RestrictedDiagnosticSeverity.Error,
                    "The parser stopped recording diagnostics after reaching its cap.",
                    token.Line,
                    token.Column));
            }
        }

        private bool PushDepth()
        {
            if (aborted)
            {
                return false;
            }

            depth++;
            if (depth <= MaximumNestingDepth)
            {
                return true;
            }

            depth--;
            aborted = true;
            Report(CodeNestingLimit, "Nesting exceeds the parser depth limit.", Current);
            return false;
        }

        private void PopDepth()
        {
            if (depth > 0)
            {
                depth--;
            }
        }

        /// <summary>
        /// Skips forward to the end of the current construct: the next <c>;</c> at the
        /// current bracket depth, or the <c>}</c> that closes the current block. The
        /// closing brace of an enclosing block is left in place for its own parser.
        /// </summary>
        private void Recover()
        {
            int nesting = 0;
            while (!AtEnd)
            {
                if (IsSymbolKind(Current.Kind))
                {
                    string text = Current.Text;
                    if (text is "{" or "(" or "[")
                    {
                        nesting++;
                        Advance();
                        continue;
                    }

                    if (text is ")" or "]")
                    {
                        if (nesting > 0)
                        {
                            nesting--;
                        }

                        Advance();
                        continue;
                    }

                    if (text == "}")
                    {
                        if (nesting == 0)
                        {
                            return;
                        }

                        nesting--;
                        Advance();
                        if (nesting == 0)
                        {
                            return;
                        }

                        continue;
                    }

                    if (text == ";" && nesting == 0)
                    {
                        Advance();
                        return;
                    }
                }

                Advance();
            }
        }

        private void ExpectSemicolon()
        {
            if (TakeSymbol(";"))
            {
                return;
            }

            Report(CodeExpectedSemicolon, "Expected ';'.", Current);
            Recover();
        }

        private static Mql5IdentifierExpression Missing(Mql5Token token) =>
            new Mql5IdentifierExpression(string.Empty, token.Line, token.Column);

        private static Mql5TypeReference ImplicitType(Mql5Token token) =>
            new(string.Empty, false, false, false, [], token.Line, token.Column);

        // ---------------------------------------------------------- entry point

        public List<Mql5Declaration> ParseCompilationUnit()
        {
            List<Mql5Declaration> declarations = [];
            while (!AtEnd)
            {
                int before = index;
                depth = 0;
                aborted = false;
                Mql5Declaration? declaration = ParseDeclaration(null);
                if (declaration is not null)
                {
                    declarations.Add(declaration);
                }

                while (pending.Count != 0)
                {
                    declarations.Add(pending.Dequeue());
                }

                if (index == before)
                {
                    Report(CodeUnexpectedToken, "Unexpected token at file scope.", Current);
                    Advance();
                    Recover();
                }
            }

            return declarations;
        }

        // --------------------------------------------------------- declarations

        /// <summary>
        /// Parses one declaration. <paramref name="enclosingType"/> is the name of the
        /// surrounding class or structure when parsing a member, otherwise null.
        /// </summary>
        private Mql5Declaration? ParseDeclaration(string? enclosingType)
        {
            if (!PushDepth())
            {
                return null;
            }

            try
            {
                return ParseDeclarationCore(enclosingType);
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Declaration? ParseDeclarationCore(string? enclosingType)
        {
            if (Current.Kind == Mql5TokenKind.PreprocessorDirective)
            {
                return ParseDirective();
            }

            if (TakeSymbol(";"))
            {
                return null;
            }

            if (AtWord("template"))
            {
                return ParseTemplate(enclosingType);
            }

            if (AtWord("typedef"))
            {
                Recover();
                return null;
            }

            if (AtWord("enum"))
            {
                return ParseEnum();
            }

            if ((AtWord("struct") || AtWord("class") || AtWord("interface") || AtWord("union"))
                && (AtName(1) || AtSymbol(1, "{")))
            {
                return ParseTypeDeclaration();
            }

            Mql5Token start = Current;
            bool isStatic = false;
            bool isConst = false;
            bool isVirtual = false;
            Mql5InputKind inputKind = Mql5InputKind.None;
            while (!AtEnd)
            {
                if (TakeWord("static"))
                {
                    isStatic = true;
                    if (inputKind == Mql5InputKind.Input)
                    {
                        inputKind = Mql5InputKind.StaticInput;
                    }

                    continue;
                }

                if (TakeWord("const"))
                {
                    isConst = true;
                    continue;
                }

                if (TakeWord("virtual"))
                {
                    isVirtual = true;
                    continue;
                }

                if (AtWord("input") || AtWord("sinput"))
                {
                    inputKind = AtWord("sinput") || isStatic ? Mql5InputKind.StaticInput : Mql5InputKind.Input;
                    Advance();

                    // 'input group "caption";' declares no entity: it is a section marker
                    // that names the group MetaTrader shows the following inputs under.
                    if (AtWord("group"))
                    {
                        Advance();

                        // Consume exactly the marker: the caption and an optional
                        // terminator. Recovering to the next ';' would be wrong, because
                        // the marker usually carries none — the next ';' belongs to the
                        // following input, and skipping to it silently discards that
                        // declaration.
                        if (Current.Kind == Mql5TokenKind.TextLiteral)
                        {
                            currentInputGroup = NormalizeLabel(Current.Value);
                            Advance();
                        }
                        else
                        {
                            currentInputGroup = null;
                        }

                        TakeSymbol(";");
                        return null;
                    }

                    continue;
                }

                if (TakeWord("extern"))
                {
                    inputKind = Mql5InputKind.Extern;
                    continue;
                }

                break;
            }

            // Destructor.
            if (AtSymbol("~") && AtName(1))
            {
                return ParseFunctionRest(ImplicitType(start), "~" + Ahead(1).Text, isStatic, isVirtual, isConst, start, skip: 2);
            }

            // Constructor: a bare name followed by an argument list inside its own type.
            if (enclosingType is not null && AtName() && AtSymbol(1, "(")
                && string.Equals(Current.Text, enclosingType, StringComparison.Ordinal))
            {
                return ParseFunctionRest(ImplicitType(start), Current.Text, isStatic, isVirtual, isConst, start, skip: 1);
            }

            int save = index;
            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                index = save;
                Report(CodeExpectedType, "Expected a type at the start of a declaration.", Current);
                Recover();
                return null;
            }

            isConst |= type.IsConst;

            // Out-of-line constructor or destructor: 'CFoo::CFoo(' / 'CFoo::~CFoo('.
            if (AtSymbol("::"))
            {
                index = save;
                string? qualified = TryReadQualifiedFunctionName();
                if (qualified is not null && AtSymbol("("))
                {
                    return ParseFunctionRest(ImplicitType(start), qualified, isStatic, isVirtual, isConst, start, skip: 0);
                }

                index = save;
                TryParseTypeReference(out type);
                if (type is null)
                {
                    Report(CodeExpectedType, "Expected a type at the start of a declaration.", Current);
                    Recover();
                    return null;
                }
            }

            if (AtWord("operator"))
            {
                string operatorName = ReadOperatorName();
                return ParseFunctionRest(type, operatorName, isStatic, isVirtual, isConst, start, skip: 0);
            }

            // No declared name follows, so the parsed type was really a constructor name.
            if (AtSymbol("("))
            {
                return ParseFunctionRest(ImplicitType(start), type.Name, isStatic, isVirtual, isConst, start, skip: 0);
            }

            string? name = TryReadDeclaredName();
            if (name is null)
            {
                Report(CodeExpectedIdentifier, "Expected a declared name.", Current);
                Recover();
                return null;
            }

            if (AtSymbol("("))
            {
                return ParseFunctionRest(type, name, isStatic, isVirtual, isConst, start, skip: 0);
            }

            List<Mql5VariableDeclarator> declarators = ParseDeclarators(name, start);

            // The dialog label is the comment trailing the terminating ';'. It is only read
            // when that semicolon is actually present, so a recovery skip cannot pick up an
            // unrelated comment further down the file.
            string? label = AtSymbol(";") ? Current.TrailingComment : null;
            ExpectSemicolon();
            return new Mql5GlobalVariableDeclaration(
                type,
                inputKind,
                isStatic,
                isConst,
                declarators,
                start.Line,
                start.Column,
                label,
                inputKind == Mql5InputKind.None ? null : currentInputGroup);
        }

        /// <summary>Reads 'operator' plus its symbol, e.g. <c>operator==</c>.</summary>
        private string ReadOperatorName()
        {
            Advance();
            if (AtSymbol("(") && AtSymbol(1, ")"))
            {
                Advance();
                Advance();
                return "operator()";
            }

            if (AtSymbol("[") && AtSymbol(1, "]"))
            {
                Advance();
                Advance();
                return "operator[]";
            }

            if (IsSymbolKind(Current.Kind))
            {
                string text = Current.Text;
                Advance();
                return "operator" + text;
            }

            return "operator";
        }

        /// <summary>Reads a possibly scope-qualified declared name such as <c>CFoo::Bar</c>.</summary>
        private string? TryReadDeclaredName()
        {
            if (!AtName())
            {
                return null;
            }

            var builder = new StringBuilder(Current.Text);
            Advance();
            while (AtSymbol("::"))
            {
                Advance();
                builder.Append("::");
                if (AtSymbol("~"))
                {
                    Advance();
                    builder.Append('~');
                }

                if (AtWord("operator"))
                {
                    builder.Append(ReadOperatorName());
                    return builder.ToString();
                }

                if (!AtName())
                {
                    return builder.ToString();
                }

                builder.Append(Current.Text);
                Advance();
            }

            return builder.ToString();
        }

        /// <summary>Recognises <c>CFoo::CFoo</c> and <c>CFoo::~CFoo</c> heads only.</summary>
        private string? TryReadQualifiedFunctionName()
        {
            if (!AtName())
            {
                return null;
            }

            string owner = Current.Text;
            Advance();
            if (!AtSymbol("::"))
            {
                return null;
            }

            Advance();
            bool destructor = TakeSymbol("~");
            if (!AtName() || !string.Equals(Current.Text, owner, StringComparison.Ordinal))
            {
                return null;
            }

            Advance();
            return destructor ? owner + "::~" + owner : owner + "::" + owner;
        }

        private Mql5FunctionDeclaration ParseFunctionRest(
            Mql5TypeReference returnType,
            string name,
            bool isStatic,
            bool isVirtual,
            bool isConst,
            Mql5Token start,
            int skip)
        {
            for (int step = 0; step < skip; step++)
            {
                Advance();
            }

            List<Mql5Parameter> parameters = ParseParameterList();
            while (AtWord("const") || AtWord("override") || AtWord("final"))
            {
                isConst |= AtWord("const");
                Advance();
            }

            // Constructor initialiser list: ': m_x(1), m_y(2)'.
            if (AtSymbol(":"))
            {
                while (!AtEnd && !aborted && !AtSymbol("{") && !AtSymbol(";"))
                {
                    Advance();
                }
            }

            // A conditional-compilation line may sit between a signature and its body.
            while (Current.Kind == Mql5TokenKind.PreprocessorDirective && !AtEnd)
            {
                Advance();
            }

            // A pure specifier such as '= 0' or '= NULL' on a declaration. This says more
            // than "no body here": MetaEditor answers an attempt to instantiate the
            // enclosing class with 'error 383: cannot instantiate abstract class', so the
            // member has no definition anywhere and is kept as abstract rather than
            // skipped into an ordinary prototype.
            bool isAbstract = false;
            if (AtSymbol("=") && !AtSymbol(1, "{"))
            {
                isAbstract = true;
                Advance();
                if (!AtSymbol(";"))
                {
                    Advance();
                }
            }

            Mql5BlockStatement? body = null;
            if (AtSymbol("{"))
            {
                body = ParseBlock();
            }
            else if (!TakeSymbol(";"))
            {
                Report(CodeExpectedSemicolon, "Expected ';' or a function body.", Current);
                Recover();
            }

            return new Mql5FunctionDeclaration(
                returnType,
                name,
                parameters,
                body,
                isStatic,
                isVirtual,
                isAbstract,
                isConst,
                start.Line,
                start.Column);
        }

        private List<Mql5Parameter> ParseParameterList()
        {
            List<Mql5Parameter> parameters = [];
            if (!Expect("(", CodeExpectedOpenParen))
            {
                return parameters;
            }

            if (TakeSymbol(")"))
            {
                return parameters;
            }

            if (AtWord("void") && AtSymbol(1, ")"))
            {
                Advance();
                Advance();
                return parameters;
            }

            while (!AtEnd && !aborted)
            {
                Mql5Token start = Current;
                if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
                {
                    Report(CodeExpectedType, "Expected a parameter type.", Current);
                    while (!AtEnd && !AtSymbol(")") && !AtSymbol(",") && !AtSymbol(";"))
                    {
                        Advance();
                    }

                    if (TakeSymbol(","))
                    {
                        continue;
                    }

                    break;
                }

                string parameterName = AtName() ? Current.Text : string.Empty;
                if (parameterName.Length != 0)
                {
                    Advance();
                }

                List<Mql5Expression?> ranks = ParseArrayRanks();
                if (ranks.Count != 0)
                {
                    type = new(type.Name, type.IsConst, type.IsPointer, type.IsReference, ranks, type.Line, type.Column);
                }

                Mql5Expression? defaultValue = null;
                if (TakeSymbol("="))
                {
                    defaultValue = ParseInitializer();
                }

                parameters.Add(new(type, parameterName, defaultValue, start.Line, start.Column));
                if (TakeSymbol(","))
                {
                    continue;
                }

                break;
            }

            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close the parameter list.", Current);
            }

            return parameters;
        }

        private List<Mql5Expression?> ParseArrayRanks()
        {
            List<Mql5Expression?> ranks = [];
            while (AtSymbol("[") && !aborted)
            {
                Advance();
                if (TakeSymbol("]"))
                {
                    ranks.Add(null);
                    continue;
                }

                ranks.Add(ParseExpression());
                if (!TakeSymbol("]"))
                {
                    Report(CodeExpectedCloseBracket, "Expected ']'.", Current);
                    break;
                }
            }

            return ranks;
        }

        private List<Mql5VariableDeclarator> ParseDeclarators(string firstName, Mql5Token start)
        {
            List<Mql5VariableDeclarator> declarators = [];
            string? name = firstName;
            Mql5Token nameToken = start;
            while (!AtEnd && !aborted)
            {
                List<Mql5Expression?> ranks = ParseArrayRanks();
                Mql5Expression? initializer = null;
                if (TakeSymbol("="))
                {
                    initializer = ParseInitializer();
                }
                else if (AtSymbol("(") && ranks.Count == 0)
                {
                    // Constructor-style initialisation is parsed and kept as a call.
                    Mql5Token open = Current;
                    List<Mql5Expression> arguments = ParseArgumentList();
                    initializer = new Mql5CallExpression(
                        new Mql5IdentifierExpression(name ?? string.Empty, open.Line, open.Column),
                        arguments,
                        open.Line,
                        open.Column);
                }

                declarators.Add(new(name ?? string.Empty, ranks, initializer, nameToken.Line, nameToken.Column));
                if (!TakeSymbol(","))
                {
                    break;
                }

                while (AtSymbol("*") || AtSymbol("&"))
                {
                    Advance();
                }

                nameToken = Current;
                if (!AtName())
                {
                    Report(CodeExpectedIdentifier, "Expected a declared name.", Current);
                    break;
                }

                name = Current.Text;
                Advance();
            }

            return declarators;
        }

        /// <summary>
        /// Reads the variables that may follow the closing brace of an enumeration or a
        /// structure, as in <c>struct Point { … } origin;</c>. They are queued as separate
        /// declarations because the tree models one declaration per node.
        /// </summary>
        private void ReadTrailingDeclarators(string typeName, Mql5Token start)
        {
            bool isPointer = false;
            while (AtSymbol("*") || AtSymbol("&"))
            {
                isPointer |= AtSymbol("*");
                Advance();
            }

            if (!AtName())
            {
                TakeSymbol(";");
                return;
            }

            Mql5Token nameToken = Current;
            string first = Current.Text;
            Advance();
            List<Mql5VariableDeclarator> declarators = ParseDeclarators(first, nameToken);
            ExpectSemicolon();
            pending.Enqueue(new Mql5GlobalVariableDeclaration(
                new(typeName, false, isPointer, false, [], start.Line, start.Column),
                Mql5InputKind.None,
                false,
                false,
                declarators,
                nameToken.Line,
                nameToken.Column));
        }

        private Mql5Expression ParseInitializer()
        {
            if (AtSymbol("{"))
            {
                return ParseInitializerList();
            }

            return ParseExpression();
        }

        private Mql5Expression ParseInitializerList()
        {
            Mql5Token open = Current;
            if (!PushDepth())
            {
                return Missing(open);
            }

            try
            {
                Advance();
                List<Mql5Expression> items = [];
                while (!AtEnd && !aborted && !AtSymbol("}"))
                {
                    int before = index;
                    items.Add(ParseInitializer());
                    if (index == before)
                    {
                        Advance();
                    }

                    if (!TakeSymbol(","))
                    {
                        break;
                    }
                }

                if (!TakeSymbol("}"))
                {
                    Report(CodeExpectedCloseBrace, "Expected '}' to close an initialiser.", Current);
                }

                return new Mql5InitializerListExpression(items, open.Line, open.Column);
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5EnumDeclaration ParseEnum()
        {
            Mql5Token start = Current;
            Advance();
            string name = string.Empty;
            if (AtName())
            {
                name = Current.Text;
                Advance();
            }

            // A scoped underlying type or forward declaration.
            if (TakeSymbol(":"))
            {
                if (AtName())
                {
                    Advance();
                }
            }

            List<Mql5EnumMemberDeclaration> members = [];
            if (TakeSymbol(";"))
            {
                return new Mql5EnumDeclaration(name, members, start.Line, start.Column);
            }

            if (!Expect("{", CodeExpectedOpenBrace))
            {
                Recover();
                return new Mql5EnumDeclaration(name, members, start.Line, start.Column);
            }

            while (!AtEnd && !aborted && !AtSymbol("}"))
            {
                if (Current.Kind == Mql5TokenKind.PreprocessorDirective)
                {
                    ParseDirective();
                    continue;
                }

                Mql5Token memberToken = Current;
                if (!AtName())
                {
                    Report(CodeExpectedIdentifier, "Expected an enumerator name.", Current);
                    break;
                }

                string memberName = Current.Text;
                Advance();
                Mql5Expression? value = null;
                if (TakeSymbol("="))
                {
                    value = ParseExpression();
                }

                members.Add(new(memberName, value, memberToken.Line, memberToken.Column));
                if (!TakeSymbol(","))
                {
                    break;
                }
            }

            if (!TakeSymbol("}"))
            {
                Report(CodeExpectedCloseBrace, "Expected '}' to close an enumeration.", Current);
                Recover();
            }
            else
            {
                ReadTrailingDeclarators(name, start);
            }

            return new Mql5EnumDeclaration(name, members, start.Line, start.Column);
        }

        private Mql5TypeDeclaration ParseTypeDeclaration()
        {
            Mql5Token start = Current;
            string keyword = Current.Text;
            Advance();
            string name = string.Empty;
            if (AtName())
            {
                name = Current.Text;
                Advance();
            }

            string? baseTypeName = null;
            if (TakeSymbol(":"))
            {
                while (AtWord("public") || AtWord("protected") || AtWord("private") || AtWord("virtual"))
                {
                    Advance();
                }

                if (AtName())
                {
                    baseTypeName = Current.Text;
                    Advance();
                    while (AtSymbol("::") && AtName(1))
                    {
                        Advance();
                        baseTypeName += "::" + Current.Text;
                        Advance();
                    }

                    if (AtSymbol("<"))
                    {
                        int save = index;
                        if (TryScanTemplateArguments(out string arguments))
                        {
                            baseTypeName += arguments;
                        }
                        else
                        {
                            index = save;
                        }
                    }
                }

                while (TakeSymbol(","))
                {
                    while (AtWord("public") || AtWord("protected") || AtWord("private") || AtWord("virtual"))
                    {
                        Advance();
                    }

                    if (AtName())
                    {
                        Advance();
                    }
                }
            }

            List<Mql5TypeMember> members = [];
            if (TakeSymbol(";"))
            {
                return new Mql5TypeDeclaration(keyword, name, baseTypeName, members, start.Line, start.Column);
            }

            if (!Expect("{", CodeExpectedOpenBrace))
            {
                Recover();
                return new Mql5TypeDeclaration(keyword, name, baseTypeName, members, start.Line, start.Column);
            }

            Mql5Access access = string.Equals(keyword, "class", StringComparison.Ordinal)
                ? Mql5Access.Private
                : Mql5Access.Public;

            while (!AtEnd && !aborted && !AtSymbol("}"))
            {
                if ((AtWord("public") || AtWord("protected") || AtWord("private")) && AtSymbol(1, ":"))
                {
                    access = AtWord("public") ? Mql5Access.Public
                        : AtWord("protected") ? Mql5Access.Protected
                        : Mql5Access.Private;
                    Advance();
                    Advance();
                    continue;
                }

                int before = index;
                Mql5Token memberStart = Current;
                Mql5Declaration? member = ParseDeclaration(name.Length == 0 ? null : name);
                if (member is not null)
                {
                    members.Add(new(access, member, memberStart.Line, memberStart.Column));
                }

                while (pending.Count != 0)
                {
                    Mql5Declaration queued = pending.Dequeue();
                    members.Add(new(access, queued, queued.Line, queued.Column));
                }

                if (index == before)
                {
                    Report(CodeExpectedDeclaration, "Expected a member declaration.", Current);
                    Advance();
                    Recover();
                }
            }

            if (!TakeSymbol("}"))
            {
                Report(CodeExpectedCloseBrace, "Expected '}' to close a type declaration.", Current);
            }
            else
            {
                ReadTrailingDeclarators(name, start);
            }

            return new Mql5TypeDeclaration(keyword, name, baseTypeName, members, start.Line, start.Column);
        }

        private Mql5TemplateDeclaration? ParseTemplate(string? enclosingType)
        {
            Mql5Token start = Current;
            Advance();
            List<string> parameters = [];
            if (Expect("<", CodeExpectedOpenParen))
            {
                while (!AtEnd && !aborted && !AtSymbol(">"))
                {
                    if (TakeWord("typename") || TakeWord("class"))
                    {
                        if (AtName())
                        {
                            parameters.Add(Current.Text);
                            Advance();
                        }
                        else
                        {
                            Report(CodeExpectedTemplateParameter, "Expected a template parameter name.", Current);
                            break;
                        }
                    }
                    else if (AtName())
                    {
                        parameters.Add(Current.Text);
                        Advance();
                    }
                    else
                    {
                        Report(CodeExpectedTemplateParameter, "Expected a template parameter.", Current);
                        break;
                    }

                    if (!TakeSymbol(","))
                    {
                        break;
                    }
                }

                if (!TakeSymbol(">"))
                {
                    Report(CodeExpectedCloseParen, "Expected '>' to close a template parameter list.", Current);
                    Recover();
                    return null;
                }
            }

            Mql5Declaration? declaration = ParseDeclaration(enclosingType);
            if (declaration is null)
            {
                return null;
            }

            return new Mql5TemplateDeclaration(parameters, declaration, start.Line, start.Column);
        }

        // ---------------------------------------------------------- directives

        private Mql5Declaration? ParseDirective()
        {
            Mql5Token start = Current;
            string text = StripTrailingComment(start.Value ?? start.Text);
            string body = text.StartsWith('#') ? text[1..] : text;
            int cut = 0;
            while (cut < body.Length && !char.IsWhiteSpace(body[cut]))
            {
                cut++;
            }

            string name = body[..cut].Trim();
            string inline = body[cut..].Trim();
            Advance();
            if (name.Length == 0 && !AtEnd && Current.Line == start.Line)
            {
                name = Current.Text;
                Advance();
            }

            switch (name)
            {
                case "property":
                {
                    string payload = ReadDirectiveTail(start, inline);
                    if (payload.Length == 0)
                    {
                        Report(CodeInvalidProperty, "A #property directive needs a name.", start);
                        return null;
                    }

                    int split = 0;
                    while (split < payload.Length && !char.IsWhiteSpace(payload[split]))
                    {
                        split++;
                    }

                    string propertyName = payload[..split];
                    string value = payload[split..].Trim();
                    return new Mql5PropertyDirective(
                        propertyName,
                        value.Length == 0 ? null : value,
                        start.Line,
                        start.Column);
                }

                case "include":
                {
                    string payload = ReadDirectiveTail(start, inline);
                    if (payload.StartsWith('<'))
                    {
                        int close = payload.IndexOf('>', StringComparison.Ordinal);
                        string path = close > 0 ? payload[1..close] : payload[1..];
                        return new Mql5IncludeDirective(path, true, start.Line, start.Column);
                    }

                    if (payload.StartsWith('"'))
                    {
                        int close = payload.IndexOf('"', 1);
                        string path = close > 0 ? payload[1..close] : payload[1..];
                        return new Mql5IncludeDirective(path, false, start.Line, start.Column);
                    }

                    Report(CodeInvalidInclude, "A #include directive needs a quoted or bracketed path.", start);
                    return null;
                }

                case "define":
                {
                    string payload = ReadDirectiveTail(start, inline);
                    int split = 0;
                    while (split < payload.Length && (char.IsLetterOrDigit(payload[split]) || payload[split] == '_'))
                    {
                        split++;
                    }

                    if (split == 0)
                    {
                        Report(CodeInvalidDefine, "A #define directive needs a macro name.", start);
                        return null;
                    }

                    if (split < payload.Length && payload[split] == '(')
                    {
                        int close = payload.IndexOf(')', split);
                        if (close > 0)
                        {
                            split = close + 1;
                        }
                    }

                    return new Mql5DefineDirective(
                        payload[..split],
                        payload[split..].Trim(),
                        start.Line,
                        start.Column);
                }

                case "import":
                    return ParseImport(start, ReadDirectiveTail(start, inline));

                default:
                    // Conditional compilation, #resource, #undef and unknown directives carry
                    // no modelled syntax; the payload is skipped without complaint.
                    ReadDirectiveTail(start, inline);
                    return null;
            }
        }

        private string ReadDirectiveTail(Mql5Token directive, string inline)
        {
            if (inline.Length != 0)
            {
                return inline;
            }

            var builder = new StringBuilder();
            int end = directive.Position + directive.Text.Length;
            while (!AtEnd && Current.Line == directive.Line)
            {
                int gap = Current.Position - end;
                if (gap > 0)
                {
                    builder.Append(' ', Math.Min(gap, 2));
                }

                builder.Append(Current.Text);
                end = Current.Position + Current.Text.Length;
                Advance();
            }

            return builder.ToString().Trim();
        }

        private Mql5ImportDirective? ParseImport(Mql5Token start, string payload)
        {
            if (payload.Length == 0)
            {
                // A closing '#import' outside an import block.
                return null;
            }

            string library = payload;
            if (library.StartsWith('"'))
            {
                int close = library.IndexOf('"', 1);
                library = close > 0 ? library[1..close] : library[1..];
            }
            else if (library.StartsWith('<'))
            {
                int close = library.IndexOf('>', StringComparison.Ordinal);
                library = close > 0 ? library[1..close] : library[1..];
            }
            else
            {
                Report(CodeInvalidImport, "An #import directive needs a library name.", start);
            }

            List<Mql5FunctionDeclaration> functions = [];
            bool closed = false;
            while (!AtEnd && !aborted)
            {
                if (Current.Kind == Mql5TokenKind.PreprocessorDirective)
                {
                    Mql5Token directive = Current;
                    string directiveText = StripTrailingComment(directive.Value ?? directive.Text);
                    string directiveBody = directiveText.StartsWith('#') ? directiveText[1..] : directiveText;
                    if (directiveBody.TrimStart().StartsWith("import", StringComparison.Ordinal))
                    {
                        string rest = directiveBody.TrimStart()["import".Length..].Trim();
                        if (rest.Length == 0)
                        {
                            Advance();
                            closed = true;
                            break;
                        }
                    }
                }

                int before = index;
                Mql5Declaration? declaration = ParseDeclaration(null);
                if (declaration is Mql5FunctionDeclaration function)
                {
                    functions.Add(function);
                }

                if (index == before)
                {
                    Advance();
                    Recover();
                }
            }

            if (!closed)
            {
                Report(CodeUnterminatedImport, "An #import block was not closed.", start);
            }

            return new Mql5ImportDirective(library, functions, start.Line, start.Column);
        }

        // ---------------------------------------------------------------- types

        /// <summary>
        /// Parses a written type. Returns false and leaves the cursor untouched when the
        /// tokens ahead cannot form one.
        /// </summary>
        private bool TryParseTypeReference(out Mql5TypeReference? type)
        {
            type = null;
            int save = index;
            Mql5Token start = Current;
            bool isConst = TakeWord("const");
            var builder = new StringBuilder();
            if (AtSymbol("::"))
            {
                Advance();
                builder.Append("::");
            }

            if (!AtName())
            {
                index = save;
                return false;
            }

            builder.Append(Current.Text);
            Advance();
            while (AtSymbol("::") && AtName(1))
            {
                Advance();
                builder.Append("::").Append(Current.Text);
                Advance();
            }

            if (AtSymbol("<"))
            {
                int mark = index;
                if (TryScanTemplateArguments(out string arguments))
                {
                    builder.Append(arguments);
                }
                else
                {
                    index = mark;
                }
            }

            isConst |= TakeWord("const");
            bool isPointer = false;
            while (AtSymbol("*"))
            {
                isPointer = true;
                Advance();
            }

            isConst |= TakeWord("const");
            bool isReference = TakeSymbol("&");
            type = new(builder.ToString(), isConst, isPointer, isReference, [], start.Line, start.Column);
            return true;
        }

        /// <summary>
        /// Consumes a balanced <c>&lt;…&gt;</c> template argument list when its content is
        /// plausibly a list of types, and reports the exact text consumed.
        /// </summary>
        private bool TryScanTemplateArguments(out string text)
        {
            text = string.Empty;
            if (!AtSymbol("<"))
            {
                return false;
            }

            var builder = new StringBuilder("<");
            int nesting = 0;
            int scanned = 0;
            int cursor = index;
            while (cursor < tokens.Count && scanned < 512)
            {
                Mql5Token token = tokens[cursor];
                if (token.Kind == Mql5TokenKind.EndOfFile)
                {
                    return false;
                }

                if (IsSymbolKind(token.Kind))
                {
                    switch (token.Text)
                    {
                        case "<":
                            nesting++;
                            break;
                        case ">":
                            nesting--;
                            if (nesting == 0)
                            {
                                index = cursor + 1;
                                builder.Append('>');
                                text = builder.ToString();
                                return true;
                            }

                            break;
                        case ">>":
                            nesting -= 2;
                            if (nesting <= 0)
                            {
                                index = cursor + 1;
                                builder.Append(">>");
                                text = builder.ToString();
                                return true;
                            }

                            break;
                        case "*":
                        case ",":
                        case "::":
                        case "&":
                            break;
                        default:
                            return false;
                    }

                    if (cursor != index)
                    {
                        builder.Append(token.Text);
                    }
                }
                else if (IsWordKind(token.Kind))
                {
                    if (NonTypeWords.Contains(token.Text) && !string.Equals(token.Text, "const", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    builder.Append(token.Text);
                }
                else if (token.Kind == Mql5TokenKind.WholeLiteral)
                {
                    builder.Append(token.Text);
                }
                else
                {
                    return false;
                }

                cursor++;
                scanned++;
            }

            return false;
        }

        // ----------------------------------------------------------- statements

        private Mql5BlockStatement ParseBlock()
        {
            Mql5Token open = Current;
            if (!PushDepth())
            {
                return new([], open.Line, open.Column);
            }

            try
            {
                Expect("{", CodeExpectedOpenBrace);
                List<Mql5Statement> statements = [];
                while (!AtEnd && !aborted && !AtSymbol("}"))
                {
                    int before = index;
                    statements.Add(ParseStatement());
                    if (index == before)
                    {
                        Report(CodeUnexpectedToken, "Unexpected token in a block.", Current);
                        Advance();
                        Recover();
                    }
                }

                if (!TakeSymbol("}"))
                {
                    Report(aborted ? CodeNestingLimit : CodeExpectedCloseBrace, "Expected '}' to close a block.", Current);
                }

                return new(statements, open.Line, open.Column);
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Statement ParseStatement()
        {
            Mql5Token start = Current;
            if (!PushDepth())
            {
                return new Mql5EmptyStatement(start.Line, start.Column);
            }

            try
            {
                return ParseStatementCore(start);
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Statement ParseStatementCore(Mql5Token start)
        {
            if (Current.Kind == Mql5TokenKind.PreprocessorDirective)
            {
                ParseDirective();
                return new Mql5EmptyStatement(start.Line, start.Column);
            }

            if (TakeSymbol(";"))
            {
                return new Mql5EmptyStatement(start.Line, start.Column);
            }

            if (AtSymbol("{"))
            {
                return ParseBlock();
            }

            if (AtWord("if"))
            {
                return ParseIf(start);
            }

            if (AtWord("for"))
            {
                return ParseFor(start);
            }

            if (AtWord("while"))
            {
                return ParseWhile(start);
            }

            if (AtWord("do"))
            {
                return ParseDoWhile(start);
            }

            if (AtWord("switch"))
            {
                return ParseSwitch(start);
            }

            if (AtWord("return"))
            {
                Advance();
                Mql5Expression? value = null;
                if (!AtSymbol(";"))
                {
                    value = ParseCommaExpression();
                }

                ExpectSemicolon();
                return new Mql5ReturnStatement(value, start.Line, start.Column);
            }

            if (AtWord("break"))
            {
                Advance();
                ExpectSemicolon();
                return new Mql5BreakStatement(start.Line, start.Column);
            }

            if (AtWord("continue"))
            {
                Advance();
                ExpectSemicolon();
                return new Mql5ContinueStatement(start.Line, start.Column);
            }

            if (AtWord("delete") && !AtSymbol(1, "("))
            {
                Advance();
                Mql5Expression operand = ParseExpression();
                ExpectSemicolon();
                return new Mql5DeleteStatement(operand, start.Line, start.Column);
            }

            if (AtWord("enum") || AtWord("struct") || AtWord("class") || AtWord("union") || AtWord("template"))
            {
                Mql5Declaration? nested = ParseDeclaration(null);
                return nested is null
                    ? new Mql5EmptyStatement(start.Line, start.Column)
                    : NestedDeclarationStatement(nested, start);
            }

            if (LooksLikeLocalDeclaration())
            {
                return ParseLocalDeclaration(start);
            }

            Mql5Expression expression = ParseCommaExpression();
            ExpectSemicolon();
            return new Mql5ExpressionStatement(expression, start.Line, start.Column);
        }

        /// <summary>
        /// The statement model carries no declaration node, so a local type declaration is
        /// recorded as an empty statement; the declaration itself is not silently dropped
        /// because the surrounding diagnostics stay clean.
        /// </summary>
        private static Mql5EmptyStatement NestedDeclarationStatement(Mql5Declaration declaration, Mql5Token start) =>
            new Mql5EmptyStatement(declaration.Line == 0 ? start.Line : declaration.Line, declaration.Column);

        private bool LooksLikeLocalDeclaration()
        {
            if (AtWord("static") || AtWord("const"))
            {
                return true;
            }

            if (!AtName())
            {
                return false;
            }

            int save = index;
            try
            {
                if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
                {
                    return false;
                }

                if (!AtName())
                {
                    return false;
                }

                Mql5Token after = Ahead(1);
                if (!IsSymbolKind(after.Kind))
                {
                    return false;
                }

                return after.Text is ";" or "=" or "," or "[";
            }
            finally
            {
                index = save;
            }
        }

        private Mql5Statement ParseLocalDeclaration(Mql5Token start)
        {
            bool isStatic = false;
            bool isConst = false;
            while (AtWord("static") || AtWord("const"))
            {
                isStatic |= AtWord("static");
                isConst |= AtWord("const");
                Advance();
            }

            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                Report(CodeExpectedType, "Expected a type in a local declaration.", Current);
                Recover();
                return new Mql5EmptyStatement(start.Line, start.Column);
            }

            isConst |= type.IsConst;
            Mql5Token nameToken = Current;
            if (!AtName())
            {
                Report(CodeExpectedIdentifier, "Expected a local variable name.", Current);
                Recover();
                return new Mql5EmptyStatement(start.Line, start.Column);
            }

            string name = Current.Text;
            Advance();
            List<Mql5VariableDeclarator> declarators = ParseDeclarators(name, nameToken);
            ExpectSemicolon();
            return new Mql5VariableDeclarationStatement(type, isStatic, isConst, declarators, start.Line, start.Column);
        }

        private Mql5IfStatement ParseIf(Mql5Token start)
        {
            Advance();
            Expect("(", CodeExpectedOpenParen);
            Mql5Expression condition = ParseCommaExpression();
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' after an if condition.", Current);
                Recover();
                return new Mql5IfStatement(condition, new Mql5EmptyStatement(start.Line, start.Column), null, start.Line, start.Column);
            }

            Mql5Statement whenTrue = ParseStatement();
            Mql5Statement? whenFalse = null;
            if (TakeWord("else"))
            {
                whenFalse = ParseStatement();
            }

            return new Mql5IfStatement(condition, whenTrue, whenFalse, start.Line, start.Column);
        }

        private Mql5WhileStatement ParseWhile(Mql5Token start)
        {
            Advance();
            Expect("(", CodeExpectedOpenParen);
            Mql5Expression condition = ParseCommaExpression();
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' after a while condition.", Current);
                Recover();
                return new Mql5WhileStatement(condition, new Mql5EmptyStatement(start.Line, start.Column), start.Line, start.Column);
            }

            Mql5Statement body = ParseStatement();
            return new Mql5WhileStatement(condition, body, start.Line, start.Column);
        }

        private Mql5DoWhileStatement ParseDoWhile(Mql5Token start)
        {
            Advance();
            Mql5Statement body = ParseStatement();
            if (!TakeWord("while"))
            {
                Report(CodeExpectedWhile, "Expected 'while' after a do body.", Current);
                Recover();
                return new Mql5DoWhileStatement(body, Missing(start), start.Line, start.Column);
            }

            Expect("(", CodeExpectedOpenParen);
            Mql5Expression condition = ParseCommaExpression();
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' after a do-while condition.", Current);
                Recover();
                return new Mql5DoWhileStatement(body, condition, start.Line, start.Column);
            }

            ExpectSemicolon();
            return new Mql5DoWhileStatement(body, condition, start.Line, start.Column);
        }

        private Mql5ForStatement ParseFor(Mql5Token start)
        {
            Advance();
            Expect("(", CodeExpectedOpenParen);
            Mql5Statement? initializer = null;
            if (!AtSymbol(";"))
            {
                Mql5Token clause = Current;
                if (LooksLikeLocalDeclaration())
                {
                    initializer = ParseLocalDeclaration(clause);
                }
                else
                {
                    Mql5Expression expression = ParseCommaExpression();
                    ExpectSemicolon();
                    initializer = new Mql5ExpressionStatement(expression, clause.Line, clause.Column);
                }
            }
            else
            {
                Advance();
            }

            Mql5Expression? condition = null;
            if (!AtSymbol(";"))
            {
                condition = ParseCommaExpression();
            }

            if (!TakeSymbol(";"))
            {
                Report(CodeExpectedSemicolon, "Expected ';' in a for clause.", Current);
                Recover();
                return new Mql5ForStatement(initializer, condition, null, new Mql5EmptyStatement(start.Line, start.Column), start.Line, start.Column);
            }

            Mql5Expression? increment = null;
            if (!AtSymbol(")"))
            {
                increment = ParseCommaExpression();
            }

            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close a for clause.", Current);
                Recover();
                return new Mql5ForStatement(initializer, condition, increment, new Mql5EmptyStatement(start.Line, start.Column), start.Line, start.Column);
            }

            Mql5Statement body = ParseStatement();
            return new Mql5ForStatement(initializer, condition, increment, body, start.Line, start.Column);
        }

        private Mql5SwitchStatement ParseSwitch(Mql5Token start)
        {
            Advance();
            Expect("(", CodeExpectedOpenParen);
            Mql5Expression subject = ParseCommaExpression();
            List<Mql5SwitchSection> sections = [];
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' after a switch subject.", Current);
                Recover();
                return new Mql5SwitchStatement(subject, sections, start.Line, start.Column);
            }

            if (!Expect("{", CodeExpectedOpenBrace))
            {
                Recover();
                return new Mql5SwitchStatement(subject, sections, start.Line, start.Column);
            }

            while (!AtEnd && !aborted && !AtSymbol("}"))
            {
                Mql5Token sectionStart = Current;
                List<Mql5Expression?> labels = [];
                while (AtWord("case") || AtWord("default"))
                {
                    bool isDefault = AtWord("default");
                    Advance();
                    if (isDefault)
                    {
                        labels.Add(null);
                    }
                    else
                    {
                        labels.Add(ParseConditional());
                    }

                    if (!TakeSymbol(":"))
                    {
                        Report(CodeExpectedColon, "Expected ':' after a switch label.", Current);
                        break;
                    }
                }

                if (labels.Count == 0)
                {
                    Report(CodeUnexpectedToken, "Expected 'case' or 'default' in a switch body.", Current);
                    Advance();
                    Recover();
                    continue;
                }

                List<Mql5Statement> statements = [];
                while (!AtEnd && !aborted && !AtSymbol("}") && !AtWord("case") && !AtWord("default"))
                {
                    int before = index;
                    statements.Add(ParseStatement());
                    if (index == before)
                    {
                        Advance();
                        Recover();
                    }
                }

                sections.Add(new(labels, statements, sectionStart.Line, sectionStart.Column));
            }

            if (!TakeSymbol("}"))
            {
                Report(CodeExpectedCloseBrace, "Expected '}' to close a switch body.", Current);
            }

            return new Mql5SwitchStatement(subject, sections, start.Line, start.Column);
        }

        // ---------------------------------------------------------- expressions

        private Mql5Expression ParseCommaExpression()
        {
            Mql5Expression left = ParseExpression();
            while (AtSymbol(",") && !aborted)
            {
                Mql5Token op = Current;
                Advance();
                Mql5Expression right = ParseExpression();
                left = new Mql5BinaryExpression(",", left, right, op.Line, op.Column);
            }

            return left;
        }

        private Mql5Expression ParseExpression()
        {
            if (!PushDepth())
            {
                return Missing(Current);
            }

            try
            {
                return ParseAssignment();
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Expression ParseAssignment()
        {
            if (!PushDepth())
            {
                return Missing(Current);
            }

            try
            {
                Mql5Expression left = ParseConditional();
                if (IsSymbolKind(Current.Kind) && AssignmentOperators.Contains(Current.Text))
                {
                    Mql5Token op = Current;
                    Advance();
                    Mql5Expression right = AtSymbol("{") ? ParseInitializerList() : ParseAssignment();
                    return new Mql5AssignmentExpression(op.Text, left, right, op.Line, op.Column);
                }

                return left;
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Expression ParseConditional()
        {
            if (!PushDepth())
            {
                return Missing(Current);
            }

            try
            {
                return ParseConditionalCore();
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Expression ParseConditionalCore()
        {
            Mql5Expression condition = ParseLogicalOr();
            if (!AtSymbol("?"))
            {
                return condition;
            }

            Mql5Token op = Current;
            Advance();
            Mql5Expression whenTrue = ParseAssignment();
            if (!TakeSymbol(":"))
            {
                Report(CodeExpectedColon, "Expected ':' in a conditional expression.", Current);
                return new Mql5ConditionalExpression(condition, whenTrue, Missing(op), op.Line, op.Column);
            }

            Mql5Expression whenFalse = ParseAssignment();
            return new Mql5ConditionalExpression(condition, whenTrue, whenFalse, op.Line, op.Column);
        }

        private Mql5Expression ParseBinary(int level)
        {
            if (level >= BinaryLevels.Length)
            {
                return ParseUnary();
            }

            if (!PushDepth())
            {
                return Missing(Current);
            }

            try
            {
                Mql5Expression left = ParseBinary(level + 1);
                while (!aborted)
                {
                    string? matched = null;
                    if (IsSymbolKind(Current.Kind))
                    {
                        foreach (string candidate in BinaryLevels[level])
                        {
                            if (string.Equals(Current.Text, candidate, StringComparison.Ordinal))
                            {
                                matched = candidate;
                                break;
                            }
                        }
                    }

                    if (matched is null)
                    {
                        break;
                    }

                    Mql5Token op = Current;
                    Advance();
                    Mql5Expression right = ParseBinary(level + 1);
                    left = new Mql5BinaryExpression(matched, left, right, op.Line, op.Column);
                }

                return left;
            }
            finally
            {
                PopDepth();
            }
        }

        private Mql5Expression ParseLogicalOr() => ParseBinary(0);

        private static readonly string[][] BinaryLevels =
        [
            ["||"],
            ["&&"],
            ["|"],
            ["^"],
            ["&"],
            ["==", "!="],
            ["<", ">", "<=", ">="],
            ["<<", ">>"],
            ["+", "-"],
            ["*", "/", "%"]
        ];

        private Mql5Expression ParseUnary()
        {
            Mql5Token start = Current;
            if (!PushDepth())
            {
                return Missing(start);
            }

            try
            {
                if (IsSymbolKind(start.Kind) && PrefixOperators.Contains(start.Text))
                {
                    Advance();
                    Mql5Expression operand = ParseUnary();
                    return new Mql5UnaryExpression(start.Text, operand, true, start.Line, start.Column);
                }

                if (AtSymbol("(") && TryParseCast(out Mql5Expression? cast) && cast is not null)
                {
                    return cast;
                }

                return ParsePostfix(ParsePrimary());
            }
            finally
            {
                PopDepth();
            }
        }

        /// <summary>
        /// Distinguishes <c>(type)expression</c> from a parenthesised expression. The
        /// content must parse as a written type followed by <c>)</c>, and the token after
        /// the close must be able to start a unary expression. A bare identifier type is
        /// only accepted when the following token cannot also be a binary operator.
        /// </summary>
        private bool TryParseCast(out Mql5Expression? expression)
        {
            expression = null;
            int save = index;
            Advance();
            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                index = save;
                return false;
            }

            List<Mql5Expression?> ranks = [];
            while (AtSymbol("[") && AtSymbol(1, "]"))
            {
                Advance();
                Advance();
                ranks.Add(null);
            }

            if (!AtSymbol(")"))
            {
                index = save;
                return false;
            }

            Advance();
            Mql5Token next = Current;
            bool decorated = type.IsPointer || type.IsReference || type.IsConst || ranks.Count != 0;
            bool builtIn = BuiltInTypeWords.Contains(type.Name);
            if (!CanStartUnary(next))
            {
                index = save;
                return false;
            }

            if (!builtIn && !decorated && IsSymbolKind(next.Kind)
                && next.Text is "-" or "+" or "&" or "*" or "(")
            {
                // '(a) - b' and '(f)(x)' are far more likely than a cast by a plain name.
                index = save;
                return false;
            }

            if (ranks.Count != 0)
            {
                type = new(type.Name, type.IsConst, type.IsPointer, type.IsReference, ranks, type.Line, type.Column);
            }

            Mql5Token start = tokens[save];
            Mql5Expression operand = ParseUnary();
            expression = new Mql5CastExpression(type, operand, start.Line, start.Column);
            return true;
        }

        private static bool CanStartUnary(Mql5Token token)
        {
            switch (token.Kind)
            {
                case Mql5TokenKind.Identifier:
                case Mql5TokenKind.WholeLiteral:
                case Mql5TokenKind.RealLiteral:
                case Mql5TokenKind.TextLiteral:
                case Mql5TokenKind.CharacterLiteral:
                case Mql5TokenKind.ColourLiteral:
                case Mql5TokenKind.DateTimeLiteral:
                    return true;
                case Mql5TokenKind.Keyword:
                    return !NonTypeWords.Contains(token.Text)
                        || token.Text is "new" or "sizeof" or "this" or "true" or "false" or "dynamic_cast" or "NULL";
                case Mql5TokenKind.Operator:
                case Mql5TokenKind.Punctuator:
                    return token.Text is "(" or "!" or "~" or "-" or "+" or "++" or "--" or "&" or "*" or "::";
                default:
                    return false;
            }
        }

        private Mql5Expression ParsePostfix(Mql5Expression target)
        {
            if (!PushDepth())
            {
                return target;
            }

            try
            {
                while (!AtEnd && !aborted)
                {
                    if (AtSymbol("("))
                    {
                        Mql5Token open = Current;
                        List<Mql5Expression> arguments = ParseArgumentList();
                        target = new Mql5CallExpression(target, arguments, open.Line, open.Column);
                        continue;
                    }

                    if (AtSymbol("["))
                    {
                        Mql5Token open = Current;
                        Advance();
                        Mql5Expression subscript = AtSymbol("]") ? Missing(open) : ParseCommaExpression();
                        if (!TakeSymbol("]"))
                        {
                            Report(CodeExpectedCloseBracket, "Expected ']'.", Current);
                            break;
                        }

                        target = new Mql5IndexExpression(target, subscript, open.Line, open.Column);
                        continue;
                    }

                    if (AtSymbol(".") || AtSymbol("->"))
                    {
                        Mql5Token op = Current;
                        bool throughPointer = string.Equals(op.Text, "->", StringComparison.Ordinal);
                        Advance();
                        string member = string.Empty;
                        if (IsWordKind(Current.Kind))
                        {
                            member = Current.Text;
                            Advance();
                        }
                        else
                        {
                            Report(CodeExpectedIdentifier, "Expected a member name.", Current);
                        }

                        target = new Mql5MemberExpression(target, member, throughPointer, op.Line, op.Column);
                        continue;
                    }

                    if (AtSymbol("::"))
                    {
                        Mql5Token op = Current;
                        Advance();
                        string member = string.Empty;
                        if (IsWordKind(Current.Kind))
                        {
                            member = Current.Text;
                            Advance();
                        }
                        else
                        {
                            Report(CodeExpectedIdentifier, "Expected a scoped name.", Current);
                        }

                        target = new Mql5ScopeExpression(target, member, op.Line, op.Column);
                        continue;
                    }

                    if (AtSymbol("++") || AtSymbol("--"))
                    {
                        Mql5Token op = Current;
                        Advance();
                        target = new Mql5UnaryExpression(op.Text, target, false, op.Line, op.Column);
                        continue;
                    }

                    break;
                }

                return target;
            }
            finally
            {
                PopDepth();
            }
        }

        private List<Mql5Expression> ParseArgumentList()
        {
            List<Mql5Expression> arguments = [];
            if (!TakeSymbol("("))
            {
                return arguments;
            }

            if (TakeSymbol(")"))
            {
                return arguments;
            }

            while (!AtEnd && !aborted)
            {
                int before = index;
                arguments.Add(ParseInitializer());
                if (index == before)
                {
                    Advance();
                }

                if (TakeSymbol(","))
                {
                    continue;
                }

                break;
            }

            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close an argument list.", Current);
            }

            return arguments;
        }

        private Mql5Expression ParsePrimary()
        {
            Mql5Token start = Current;
            switch (start.Kind)
            {
                case Mql5TokenKind.WholeLiteral:
                    Advance();
                    return new Mql5LiteralExpression(Mql5LiteralKind.Whole, start.Text, start.Line, start.Column);
                case Mql5TokenKind.RealLiteral:
                    Advance();
                    return new Mql5LiteralExpression(Mql5LiteralKind.Real, start.Text, start.Line, start.Column);
                case Mql5TokenKind.TextLiteral:
                {
                    Advance();
                    var builder = new StringBuilder(start.Text);
                    while (Current.Kind == Mql5TokenKind.TextLiteral)
                    {
                        builder.Append(Current.Text);
                        Advance();
                    }

                    return new Mql5LiteralExpression(Mql5LiteralKind.Text, builder.ToString(), start.Line, start.Column);
                }

                case Mql5TokenKind.CharacterLiteral:
                    Advance();
                    return new Mql5LiteralExpression(Mql5LiteralKind.Character, start.Text, start.Line, start.Column);
                case Mql5TokenKind.ColourLiteral:
                    Advance();
                    return new Mql5LiteralExpression(Mql5LiteralKind.Colour, start.Text, start.Line, start.Column);
                case Mql5TokenKind.DateTimeLiteral:
                    Advance();
                    return new Mql5LiteralExpression(Mql5LiteralKind.DateTime, start.Text, start.Line, start.Column);
                default:
                    break;
            }

            if (AtSymbol("("))
            {
                Advance();
                Mql5Expression inner = ParseCommaExpression();
                if (!TakeSymbol(")"))
                {
                    Report(CodeExpectedCloseParen, "Expected ')' to close a parenthesised expression.", Current);
                }

                return inner;
            }

            if (AtSymbol("{"))
            {
                return ParseInitializerList();
            }

            if (AtSymbol("::"))
            {
                Advance();
                string name = string.Empty;
                if (IsWordKind(Current.Kind))
                {
                    name = Current.Text;
                    Advance();
                }
                else
                {
                    Report(CodeExpectedIdentifier, "Expected a name after '::'.", Current);
                }

                return new Mql5ScopeExpression(null, name, start.Line, start.Column);
            }

            if (IsWordKind(start.Kind))
            {
                switch (start.Text)
                {
                    case "true":
                    case "false":
                        Advance();
                        return new Mql5LiteralExpression(Mql5LiteralKind.Boolean, start.Text, start.Line, start.Column);
                    case "NULL":
                    case "null":
                        Advance();
                        return new Mql5LiteralExpression(Mql5LiteralKind.Null, start.Text, start.Line, start.Column);
                    case "new":
                        return ParseNew(start);
                    case "sizeof":
                        return ParseSizeOf(start);
                    case "dynamic_cast":
                        return ParseDynamicCast(start);
                    case "typename":
                        return ParseTypeName(start);
                    case "delete":
                        Advance();
                        return new Mql5UnaryExpression("delete", ParseUnary(), true, start.Line, start.Column);
                    default:
                        break;
                }

                if (!NonTypeWords.Contains(start.Text) || string.Equals(start.Text, "this", StringComparison.Ordinal))
                {
                    Advance();
                    return new Mql5IdentifierExpression(start.Text, start.Line, start.Column);
                }
            }

            Report(CodeUnexpectedToken, "Expected an expression.", start);
            if (!AtEnd)
            {
                Advance();
            }
            else
            {
                Report(CodeUnexpectedEnd, "The file ended inside an expression.", start);
                aborted = true;
            }

            return Missing(start);
        }

        private Mql5Expression ParseNew(Mql5Token start)
        {
            Advance();
            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                Report(CodeExpectedType, "Expected a type after 'new'.", Current);
                return Missing(start);
            }

            List<Mql5Expression?> ranks = ParseArrayRanks();
            if (ranks.Count != 0)
            {
                type = new(type.Name, type.IsConst, type.IsPointer, type.IsReference, ranks, type.Line, type.Column);
            }

            Mql5Expression created = new Mql5NewExpression(type, start.Line, start.Column);
            if (AtSymbol("("))
            {
                Mql5Token open = Current;
                List<Mql5Expression> arguments = ParseArgumentList();
                created = new Mql5CallExpression(created, arguments, open.Line, open.Column);
            }

            return created;
        }

        private Mql5Expression ParseSizeOf(Mql5Token start)
        {
            Advance();
            if (!Expect("(", CodeExpectedOpenParen))
            {
                return Missing(start);
            }

            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                Report(CodeExpectedType, "Expected a type or name in 'sizeof'.", Current);
                Recover();
                return Missing(start);
            }

            List<Mql5Expression?> ranks = ParseArrayRanks();
            if (ranks.Count != 0)
            {
                type = new(type.Name, type.IsConst, type.IsPointer, type.IsReference, ranks, type.Line, type.Column);
            }

            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close 'sizeof'.", Current);
            }

            // MQL5 measures a variable as readily as a type, and an undecorated bare name is
            // both grammars at once: `sizeof(post)` in the corpus is a `char post[]` local, not
            // a structure called post. The parser has no symbol table and must not choose, so
            // it records the name in both forms and leaves the choice to a pass that does.
            // Anything a value cannot be — a built-in scalar keyword, an array suffix, a handle,
            // a `const` — takes the type form alone.
            return new Mql5SizeOfExpression(type, start.Line, start.Column)
            {
                Operand = !IsUnmistakablyAType(type)
                    && !type.IsConst
                    && type.Name.Length != 0
                    && !type.Name.Contains(':', StringComparison.Ordinal)
                    ? new Mql5IdentifierExpression(type.Name, type.Line, type.Column)
                    : null
            };
        }


        /// <summary>
        /// Parses the <c>typename</c> operator, which MQL5 lets take either a written
        /// type or an expression.
        ///
        /// The two are told apart only where the grammars genuinely differ: a built-in
        /// scalar keyword, a handle, or an array suffix cannot be an expression, so those
        /// take the type form. Everything else — including a bare name, which is the only
        /// form the corpus uses — is parsed as an expression and left for the binder,
        /// which already knows whether a name denotes a type or a value. Guessing here
        /// would mean deciding a semantic question with no symbol table to answer it.
        /// </summary>
        private Mql5Expression ParseTypeName(Mql5Token start)
        {
            Advance();
            if (!Expect("(", CodeExpectedOpenParen))
            {
                return Missing(start);
            }

            int save = index;
            if (TryParseTypeReference(out Mql5TypeReference? type) && type is not null)
            {
                List<Mql5Expression?> ranks = ParseArrayRanks();
                if (ranks.Count != 0)
                {
                    type = new(type.Name, type.IsConst, type.IsPointer, type.IsReference, ranks, type.Line, type.Column);
                }

                if (AtSymbol(")") && IsUnmistakablyAType(type))
                {
                    Advance();
                    return new Mql5TypeNameExpression(type, null, start.Line, start.Column);
                }
            }

            index = save;
            Mql5Expression operand = ParseExpression();
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close 'typename'.", Current);
            }

            return new Mql5TypeNameExpression(null, operand, start.Line, start.Column);
        }

        /// <summary>
        /// True when a parsed type reference cannot also be read as an expression: a
        /// built-in scalar keyword, or any name carrying a handle, reference or array
        /// decoration. A bare user-defined name is deliberately excluded — it is
        /// indistinguishable from a variable until names are resolved.
        /// </summary>
        private static bool IsUnmistakablyAType(Mql5TypeReference type) =>
            Mql5IrLiteral.ClassifyScalar(type.Name) != Mql5IrScalarKind.None
            || type.IsPointer
            || type.IsReference
            || type.ArrayRanks.Count > 0;
        private Mql5Expression ParseDynamicCast(Mql5Token start)
        {
            Advance();
            if (!Expect("<", CodeExpectedOpenParen))
            {
                return Missing(start);
            }

            if (!TryParseTypeReference(out Mql5TypeReference? type) || type is null)
            {
                Report(CodeExpectedType, "Expected a type in 'dynamic_cast'.", Current);
                Recover();
                return Missing(start);
            }

            if (!TakeSymbol(">"))
            {
                Report(CodeExpectedCloseParen, "Expected '>' in 'dynamic_cast'.", Current);
            }

            if (!Expect("(", CodeExpectedOpenParen))
            {
                return Missing(start);
            }

            Mql5Expression operand = ParseCommaExpression();
            if (!TakeSymbol(")"))
            {
                Report(CodeExpectedCloseParen, "Expected ')' to close 'dynamic_cast'.", Current);
            }

            return new Mql5CastExpression(type, operand, start.Line, start.Column);
        }
    }
}
