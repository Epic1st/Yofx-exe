using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace YO4X.StrategyGovernance;

/// <summary>
/// Lexical categories produced by <see cref="Mql5Lexer"/>.
///
/// The literal kinds mirror <see cref="Mql5LiteralKind"/>: <c>Whole</c> is an integer
/// literal, <c>Real</c> a floating-point literal and <c>Text</c> a string literal. The
/// names deliberately avoid CLR type names.
/// </summary>
public enum Mql5TokenKind
{
    /// <summary>A user-defined name.</summary>
    Identifier,

    /// <summary>A reserved word or built-in type name.</summary>
    Keyword,

    /// <summary>An integer literal such as <c>42</c> or <c>0x2A</c>.</summary>
    WholeLiteral,

    /// <summary>A floating-point literal such as <c>1.5</c> or <c>2e-3f</c>.</summary>
    RealLiteral,

    /// <summary>A double-quoted string literal.</summary>
    TextLiteral,

    /// <summary>A single-quoted character literal.</summary>
    CharacterLiteral,

    /// <summary>A colour literal such as <c>C'255,128,0'</c>.</summary>
    ColourLiteral,

    /// <summary>A date literal such as <c>D'2024.01.31 12:00'</c>.</summary>
    DateTimeLiteral,

    /// <summary>An operator such as <c>+=</c>, <c>::</c> or <c>-&gt;</c>.</summary>
    Operator,

    /// <summary>A structural symbol: <c>( ) [ ] { } , ; :</c>.</summary>
    Punctuator,

    /// <summary>A whole preprocessor line, leading <c>#</c> included.</summary>
    PreprocessorDirective,

    /// <summary>The synthetic end-of-input token that always terminates the stream.</summary>
    EndOfFile
}

/// <summary>
/// One lexical token.
/// </summary>
/// <param name="Kind">The lexical category.</param>
/// <param name="Text">The verbatim source slice, delimiters and escapes included.</param>
/// <param name="Value">
/// The decoded payload: unescaped string and character contents, normalised numeric text,
/// <c>r,g,b</c> for colours, <c>yyyy.MM.dd HH:mm:ss</c> for dates and the logical
/// (continuation-joined) line for preprocessor directives. Null for every other kind.
/// </param>
/// <param name="Line">The 1-based line of the first character.</param>
/// <param name="Column">The 1-based column, counted in UTF-16 code units, of the first character.</param>
/// <param name="Position">The 0-based offset of the first character within the source.</param>
/// <param name="TrailingComment">
/// Comment trivia: the last comment that follows this token on the same physical line with
/// no intervening token, with the comment markers removed, interior whitespace collapsed to
/// single spaces and the result trimmed. Null when the token carries no such comment or
/// the comment is blank. Comments are never emitted as tokens, so this trivia leaves the
/// token stream — and therefore the parser — unchanged. MetaTrader renders exactly this
/// text as the label of an <c>input</c> in the strategy properties dialog.
/// </param>
public sealed record Mql5Token(
    Mql5TokenKind Kind,
    string Text,
    string? Value,
    int Line,
    int Column,
    int Position,
    string? TrailingComment = null);

/// <summary>The outcome of one lexing pass. The token list always ends with an end-of-file token.</summary>
public sealed record Mql5LexResult(
    IReadOnlyList<Mql5Token> Tokens,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics);

/// <summary>
/// The MQL5 lexer for the full language, as opposed to the data-only restricted subset
/// handled by <see cref="Mql5RestrictedSubsetCompiler"/>.
///
/// It runs over untrusted third-party sources: it never throws on malformed input, never
/// loops unboundedly and reports every problem it meets as a diagnostic while still
/// producing a usable token stream so the parser can recover.
/// </summary>
public static class Mql5Lexer
{
    /// <summary>Reported when a string literal is not closed before the end of its line or the input.</summary>
    public const string UnterminatedTextCode = "MQL5_LEX_UNTERMINATED_STRING";

    /// <summary>Reported when a character literal is not closed before the end of its line or the input.</summary>
    public const string UnterminatedCharacterCode = "MQL5_LEX_UNTERMINATED_CHARACTER";

    /// <summary>Reported when a <c>C'…'</c> or <c>D'…'</c> literal is not closed.</summary>
    public const string UnterminatedLiteralCode = "MQL5_LEX_UNTERMINATED_LITERAL";

    /// <summary>Reported when a block comment is not closed before the end of the input.</summary>
    public const string UnterminatedCommentCode = "MQL5_LEX_UNTERMINATED_COMMENT";

    /// <summary>Reported for a source character that cannot begin any token.</summary>
    public const string InvalidCharacterCode = "MQL5_LEX_INVALID_CHARACTER";

    /// <summary>Reported for a malformed numeric literal.</summary>
    public const string InvalidNumberCode = "MQL5_LEX_INVALID_NUMBER";

    /// <summary>Reported for an empty character literal.</summary>
    public const string InvalidCharacterLiteralCode = "MQL5_LEX_INVALID_CHARACTER_LITERAL";

    /// <summary>Reported for a colour literal whose components are not three byte values.</summary>
    public const string InvalidColourCode = "MQL5_LEX_INVALID_COLOUR";

    /// <summary>Reported for a date literal that is not a valid date and/or time.</summary>
    public const string InvalidDateTimeCode = "MQL5_LEX_INVALID_DATETIME";

    /// <summary>Reported when the source, token or diagnostic limit is reached.</summary>
    public const string LimitExceededCode = "MQL5_LEX_LIMIT_EXCEEDED";

    /// <summary>Mirrors the 16 MiB source cap of the restricted compiler, measured in UTF-16 code units.</summary>
    public const int MaximumSourceCharacters = 16 * 1024 * 1024;

    /// <summary>Mirrors the two million token cap of the restricted compiler.</summary>
    public const int MaximumTokens = 2_000_000;

    /// <summary>
    /// The longest comment retained as trailing trivia. A dialog label is a short phrase;
    /// the cap keeps a stray multi-line block comment from being carried through the IR.
    /// </summary>
    public const int MaximumCommentCharacters = 512;

    private const int MaximumDiagnostics = 512;

    /// <summary>
    /// The MQL5 reserved words: the documented keyword set plus the built-in type names
    /// (<c>uchar</c> … <c>ulong</c>, <c>datetime</c>, <c>color</c>) and the built-in
    /// matrix, vector and complex types. MQL5 is case-sensitive, so lookups are ordinal.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(
        [
            // Built-in scalar types and their unsigned aliases.
            "bool", "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "string", "datetime", "color", "void",

            // Built-in composite types.
            "matrix", "matrixf", "matrixc", "vector", "vectorf", "vectorc", "complex",

            // User type declarations.
            "enum", "struct", "class", "interface", "union", "template", "typename", "typedef",

            // Access and storage.
            "public", "protected", "private", "virtual", "override", "final",
            "const", "static", "extern", "input", "sinput",

            // Operators spelled as words.
            "operator", "new", "delete", "sizeof", "dynamic_cast", "this",

            // Control flow.
            "if", "else", "switch", "case", "default", "for", "while", "do",
            "break", "continue", "return",

            // Constants.
            "true", "false", "NULL",

            // Module boundaries.
            "import", "export"
        ],
        StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> NamedColours = BuildNamedColours();

    private static FrozenDictionary<string, string> BuildNamedColours()
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Mql5BuiltinConstant constant in Mql5BuiltinConstants.All)
        {
            if (constant.Name.StartsWith("clr", StringComparison.Ordinal)
                && constant.Name.Length > 3
                && constant.Value is long value
                && value >= 0
                && value <= 0xFFFFFF)
            {
                int r = (int)(value & 0xFF);
                int g = (int)((value >> 8) & 0xFF);
                int b = (int)((value >> 16) & 0xFF);
                string normalised = string.Create(CultureInfo.InvariantCulture, $"{r},{g},{b}");
                dictionary[constant.Name] = normalised;
                dictionary[constant.Name[3..]] = normalised;
            }
        }

        return dictionary.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tokenizes an MQL5 translation unit. The returned token list always ends with an
    /// <see cref="Mql5TokenKind.EndOfFile"/> token, even when diagnostics were produced.
    /// </summary>
    public static Mql5LexResult Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumSourceCharacters)
        {
            return new(
                [new(Mql5TokenKind.EndOfFile, string.Empty, null, 1, 1, 0)],
                [
                    new(
                        LimitExceededCode,
                        Mql5RestrictedDiagnosticSeverity.Error,
                        "Source exceeds the 16 MiB lexer limit.",
                        1,
                        1)
                ]);
        }

        var scanner = new Scanner(source);
        scanner.Run();
        return new(scanner.Tokens, scanner.Diagnostics);
    }

    private sealed class Scanner(string source)
    {
        private static readonly char[] TimeSeparators = [':'];
        private static readonly char[] DateSeparators = ['.', '/', '-'];
        private static readonly char[] BlankSeparators = [' ', '\t'];
        private static readonly char[] ComponentSeparators = [','];
        private readonly List<Mql5Token> tokens = [];
        private readonly List<Mql5RestrictedDiagnostic> diagnostics = [];
        private int index;
        private int line = 1;
        private int column = 1;
        private bool atLineStart = true;
        private bool diagnosticLimitReported;

        /// <summary>
        /// The physical line on which the most recently added token ended. A comment is
        /// trailing trivia of that token only when it opens on this same line, which is
        /// why the end line rather than the token's start line is tracked: a string,
        /// directive or spliced construct may span several lines.
        /// </summary>
        private int lastTokenEndLine;

        public List<Mql5Token> Tokens => tokens;

        public List<Mql5RestrictedDiagnostic> Diagnostics => diagnostics;

        public void Run()
        {
            while (index < source.Length)
            {
                if (tokens.Count >= MaximumTokens)
                {
                    AddDiagnostic(LimitExceededCode, "Token limit exceeded.", line, column);
                    break;
                }

                char current = source[index];
                if (char.IsWhiteSpace(current))
                {
                    Advance();
                    continue;
                }

                if (current == '/' && Peek(1) == '/')
                {
                    ReadLineComment();
                    continue;
                }

                if (current == '/' && Peek(1) == '*')
                {
                    ReadBlockComment();
                    continue;
                }

                if (current == '#' && atLineStart)
                {
                    ReadDirective();
                    continue;
                }

                if (current is 'C' or 'c' or 'D' or 'd' && Peek(1) == '\'')
                {
                    ReadPrefixedLiteral(char.ToUpperInvariant(current));
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    ReadIdentifier();
                    continue;
                }

                if (char.IsAsciiDigit(current)
                    || (current == '.' && char.IsAsciiDigit(Peek(1))))
                {
                    ReadNumber();
                    continue;
                }

                if (current == '"')
                {
                    ReadQuotedLiteral(
                        '"',
                        Mql5TokenKind.TextLiteral,
                        UnterminatedTextCode,
                        "String literal is not terminated.");
                    continue;
                }

                if (current == '\'')
                {
                    ReadQuotedLiteral(
                        '\'',
                        Mql5TokenKind.CharacterLiteral,
                        UnterminatedCharacterCode,
                        "Character literal is not terminated.");
                    continue;
                }

                if (current == '\\' && TrySkipLineSplice())
                {
                    continue;
                }

                if (TryReadOperator())
                {
                    continue;
                }

                ReadInvalidCharacter();
            }

            tokens.Add(new(Mql5TokenKind.EndOfFile, string.Empty, null, line, column, index));
        }

        // ------------------------------------------------------------- scanning

        private void ReadLineComment()
        {
            int startLine = line;
            int startIndex = index;
            while (index < source.Length && !IsNewLine(source[index]))
            {
                Advance();
            }

            // The slice always opens with '//'; the body is what follows it.
            AttachTrailingComment(startLine, source[(startIndex + 2)..index]);
        }

        private void ReadBlockComment()
        {
            int startLine = line;
            int startColumn = column;
            int startIndex = index;
            Advance();
            Advance();
            while (index < source.Length)
            {
                if (source[index] == '*' && Peek(1) == '/')
                {
                    Advance();
                    Advance();

                    // Strip the leading '/*' and the trailing '*/'.
                    AttachTrailingComment(startLine, source[(startIndex + 2)..(index - 2)]);
                    return;
                }

                Advance();
            }

            AddDiagnostic(
                UnterminatedCommentCode,
                "Block comment is not terminated.",
                startLine,
                startColumn);
            AttachTrailingComment(startLine, source[(startIndex + 2)..index]);
        }

        /// <summary>
        /// Records a comment as trailing trivia on the token it follows, when the comment
        /// opens on the same physical line on which that token ended.
        ///
        /// When several comments trail the same token the last one wins. Sources converted
        /// from MQL4 commonly write <c>input int t1 = 6; /*t1*/ // shift of one bar</c>,
        /// where the block comment merely repeats the declared name and the line comment
        /// carries the description MetaTrader shows.
        ///
        /// A blank comment never displaces a recorded one, and nothing is added to the
        /// token stream: the parser sees exactly the tokens it saw before trivia existed.
        /// </summary>
        private void AttachTrailingComment(int commentLine, string body)
        {
            if (tokens.Count == 0 || lastTokenEndLine != commentLine)
            {
                return;
            }

            Mql5Token previous = tokens[^1];
            string text = NormalizeComment(body);
            if (text.Length == 0)
            {
                return;
            }

            tokens[^1] = previous with { TrailingComment = text };
        }

        /// <summary>
        /// Collapses every run of whitespace or control characters in a comment body to a
        /// single space and trims the ends, so a label is stable regardless of the padding
        /// the author used to align it. The text is otherwise verbatim: nothing is added,
        /// reworded or inferred. A body longer than <see cref="MaximumCommentCharacters"/>
        /// is truncated.
        /// </summary>
        private static string NormalizeComment(string body)
        {
            var builder = new StringBuilder(Math.Min(body.Length, MaximumCommentCharacters));
            bool separated = false;
            for (int cursor = 0; cursor < body.Length; cursor++)
            {
                char current = body[cursor];
                bool paired =
                    char.IsHighSurrogate(current)
                    && cursor + 1 < body.Length
                    && char.IsLowSurrogate(body[cursor + 1]);

                // Drop blanks, control characters and unpaired surrogates; a valid
                // surrogate pair is one character and is carried through intact.
                if (!paired && (char.IsWhiteSpace(current) || char.IsControl(current) || char.IsSurrogate(current)))
                {
                    separated = builder.Length > 0;
                    continue;
                }

                if (builder.Length >= MaximumCommentCharacters)
                {
                    break;
                }

                if (separated)
                {
                    builder.Append(' ');
                    separated = false;
                }

                builder.Append(current);
                if (paired)
                {
                    cursor++;
                    builder.Append(body[cursor]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reads one logical preprocessor line. A backslash immediately before a line break
        /// continues the directive onto the next line; the token text keeps the raw source
        /// slice while the value holds the joined single-line form.
        /// </summary>
        private void ReadDirective()
        {
            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            var logical = new StringBuilder();
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '\\' && TrySkipLineSplice())
                {
                    logical.Append(' ');
                    continue;
                }

                if (IsNewLine(current))
                {
                    break;
                }

                logical.Append(current);
                Advance();
            }

            AddToken(
                Mql5TokenKind.PreprocessorDirective,
                source[startIndex..index],
                logical.ToString().TrimEnd(),
                startLine,
                startColumn,
                startIndex);
        }

        /// <summary>
        /// Consumes a backslash that is followed only by blanks and a line break: the C line
        /// splice, which MQL5 honours inside macros and — as the corpus shows — in ordinary
        /// code too. The spliced line continues the current logical line.
        /// </summary>
        private bool TrySkipLineSplice()
        {
            int probe = index + 1;
            while (probe < source.Length && source[probe] is ' ' or '\t')
            {
                probe++;
            }

            if (probe >= source.Length || !IsNewLine(source[probe]))
            {
                return false;
            }

            bool wasAtLineStart = atLineStart;
            while (index < probe)
            {
                Advance();
            }

            Advance();
            atLineStart = wasAtLineStart;
            return true;
        }

        private void ReadIdentifier()
        {
            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            while (index < source.Length && IsIdentifierPart(source[index]))
            {
                Advance();
            }

            string text = source[startIndex..index];
            AddToken(
                Keywords.Contains(text) ? Mql5TokenKind.Keyword : Mql5TokenKind.Identifier,
                text,
                null,
                startLine,
                startColumn,
                startIndex);
        }

        private void ReadNumber()
        {
            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            bool isReal = false;
            bool malformed = false;
            string? value;

            if (source[index] == '0' && Peek(1) is 'x' or 'X')
            {
                Advance();
                Advance();
                int digitsStart = index;
                while (index < source.Length && Uri.IsHexDigit(source[index]))
                {
                    Advance();
                }

                string digits = source[digitsStart..index];
                if (digits.Length == 0)
                {
                    malformed = true;
                    value = "0";
                }
                else if (ulong.TryParse(
                    digits,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong parsed))
                {
                    value = parsed.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    malformed = true;
                    value = digits;
                }

                ConsumeWholeSuffix();
            }
            else
            {
                while (index < source.Length && char.IsAsciiDigit(source[index]))
                {
                    Advance();
                }

                if (index < source.Length && source[index] == '.' && FractionFollows())
                {
                    isReal = true;
                    Advance();
                    while (index < source.Length && char.IsAsciiDigit(source[index]))
                    {
                        Advance();
                    }
                }

                if (index < source.Length && source[index] is 'e' or 'E' && ExponentFollows())
                {
                    isReal = true;
                    Advance();
                    if (source[index] is '+' or '-')
                    {
                        Advance();
                    }

                    while (index < source.Length && char.IsAsciiDigit(source[index]))
                    {
                        Advance();
                    }
                }

                value = source[startIndex..index];
                if (index < source.Length && source[index] is 'f' or 'F')
                {
                    isReal = true;
                    Advance();
                }
                else if (!isReal)
                {
                    ConsumeWholeSuffix();
                }
            }

            if (index < source.Length && IsIdentifierPart(source[index]))
            {
                malformed = true;
                while (index < source.Length && IsIdentifierPart(source[index]))
                {
                    Advance();
                }
            }

            string text = source[startIndex..index];
            if (malformed)
            {
                AddDiagnostic(
                    InvalidNumberCode,
                    $"Numeric literal '{text}' is malformed.",
                    startLine,
                    startColumn);
            }

            AddToken(
                isReal ? Mql5TokenKind.RealLiteral : Mql5TokenKind.WholeLiteral,
                text,
                value,
                startLine,
                startColumn,
                startIndex);
        }

        /// <summary>
        /// Decides whether a <c>.</c> that follows digits opens a fraction. Member access on
        /// an index expression such as <c>rates[0].close</c> must not be swallowed.
        /// </summary>
        private bool FractionFollows()
        {
            char next = Peek(1);
            if (char.IsAsciiDigit(next))
            {
                return true;
            }

            if (next is 'e' or 'E')
            {
                return char.IsAsciiDigit(Peek(2)) || (Peek(2) is '+' or '-' && char.IsAsciiDigit(Peek(3)));
            }

            if (next is 'f' or 'F')
            {
                return !IsIdentifierPart(Peek(2));
            }

            return !IsIdentifierStart(next) && next != '.';
        }

        private bool ExponentFollows()
        {
            char next = Peek(1);
            return char.IsAsciiDigit(next) || (next is '+' or '-' && char.IsAsciiDigit(Peek(2)));
        }

        private void ConsumeWholeSuffix()
        {
            while (index < source.Length && source[index] is 'u' or 'U' or 'l' or 'L')
            {
                Advance();
            }
        }

        /// <summary>Reads a string or character literal, decoding escapes into the token value.</summary>
        private void ReadQuotedLiteral(
            char delimiter,
            Mql5TokenKind kind,
            string unterminatedCode,
            string unterminatedMessage)
        {
            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            Advance();
            var decoded = new StringBuilder();
            bool closed = false;
            while (index < source.Length)
            {
                char current = source[index];
                if (IsNewLine(current))
                {
                    break;
                }

                if (current == delimiter)
                {
                    Advance();
                    closed = true;
                    break;
                }

                if (current == '\\' && TrySkipLineSplice())
                {
                    continue;
                }

                if (current == '\\')
                {
                    AppendEscape(decoded);
                    continue;
                }

                decoded.Append(current);
                Advance();
            }

            if (!closed)
            {
                AddDiagnostic(unterminatedCode, unterminatedMessage, startLine, startColumn);
            }
            else if (kind == Mql5TokenKind.CharacterLiteral && decoded.Length == 0)
            {
                AddDiagnostic(
                    InvalidCharacterLiteralCode,
                    "Character literal is empty.",
                    startLine,
                    startColumn);
            }

            AddToken(
                kind,
                source[startIndex..index],
                decoded.ToString(),
                startLine,
                startColumn,
                startIndex);
        }

        /// <summary>
        /// Decodes one escape sequence. Unknown escapes keep their character verbatim rather
        /// than raising a diagnostic: unescaped Windows paths are common in real sources.
        /// </summary>
        private void AppendEscape(StringBuilder decoded)
        {
            Advance();
            if (index >= source.Length)
            {
                decoded.Append('\\');
                return;
            }

            char current = source[index];
            if (IsNewLine(current))
            {
                // MQL5 has no multi-line string literals; leave the break to close the literal.
                decoded.Append('\\');
                return;
            }

            if (current is >= '0' and <= '7')
            {
                int value = 0;
                int digits = 0;
                while (digits < 3 && index < source.Length && source[index] is >= '0' and <= '7')
                {
                    value = (value * 8) + (source[index] - '0');
                    digits++;
                    Advance();
                }

                decoded.Append((char)(value & 0xFFFF));
                return;
            }

            if (current is 'x' or 'X' or 'u' or 'U')
            {
                int maximumDigits = current == 'U' ? 8 : 4;
                Advance();
                long value = 0;
                int digits = 0;
                while (digits < maximumDigits && index < source.Length && Uri.IsHexDigit(source[index]))
                {
                    value = (value * 16) + Uri.FromHex(source[index]);
                    digits++;
                    Advance();
                }

                if (digits == 0)
                {
                    decoded.Append(current);
                    return;
                }

                if (value is > 0xFFFF and <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF))
                {
                    decoded.Append(char.ConvertFromUtf32((int)value));
                }
                else
                {
                    decoded.Append((char)(value & 0xFFFF));
                }

                return;
            }

            char replacement = current switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                _ => current
            };
            decoded.Append(replacement);
            Advance();
        }

        /// <summary>Reads a <c>C'…'</c> colour literal or a <c>D'…'</c> date literal.</summary>
        private void ReadPrefixedLiteral(char prefix)
        {
            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            Advance();
            Advance();
            int contentStart = index;
            while (index < source.Length && source[index] != '\'' && !IsNewLine(source[index]))
            {
                Advance();
            }

            int contentEnd = index;
            bool closed = index < source.Length && source[index] == '\'';
            if (closed)
            {
                Advance();
            }
            else
            {
                AddDiagnostic(
                    UnterminatedLiteralCode,
                    prefix == 'C'
                        ? "Colour literal is not terminated."
                        : "Date literal is not terminated.",
                    startLine,
                    startColumn);
            }

            string content = source[contentStart..contentEnd];
            string? normalised = prefix == 'C'
                ? NormaliseColour(content)
                : NormaliseDateTime(content);
            if (normalised is null)
            {
                AddDiagnostic(
                    prefix == 'C' ? InvalidColourCode : InvalidDateTimeCode,
                    prefix == 'C'
                        ? $"Colour literal '{content}' is not three byte components."
                        : $"Date literal '{content}' is not a valid date and time.",
                    startLine,
                    startColumn);
            }

            AddToken(
                prefix == 'C' ? Mql5TokenKind.ColourLiteral : Mql5TokenKind.DateTimeLiteral,
                source[startIndex..index],
                normalised ?? content,
                startLine,
                startColumn,
                startIndex);
        }

        private bool TryReadOperator()
        {
            int length = OperatorLength();
            if (length == 0)
            {
                return false;
            }

            int startIndex = index;
            int startLine = line;
            int startColumn = column;
            for (int step = 0; step < length; step++)
            {
                Advance();
            }

            string text = source[startIndex..index];
            AddToken(
                length == 1 && IsPunctuator(text[0]) ? Mql5TokenKind.Punctuator : Mql5TokenKind.Operator,
                text,
                null,
                startLine,
                startColumn,
                startIndex);
            return true;
        }

        /// <summary>Longest-match operator and punctuator recognition. Returns 0 for a foreign character.</summary>
        private int OperatorLength()
        {
            char first = source[index];
            char second = Peek(1);
            char third = Peek(2);
            return first switch
            {
                '>' when second == '>' && third == '=' => 3,
                '<' when second == '<' && third == '=' => 3,
                '.' when second == '.' && third == '.' => 3,
                '>' => second is '>' or '=' ? 2 : 1,
                '<' => second is '<' or '=' ? 2 : 1,
                '+' => second is '+' or '=' ? 2 : 1,
                '-' => second is '-' or '=' or '>' ? 2 : 1,
                '*' or '/' or '%' or '^' or '=' or '!' => second == '=' ? 2 : 1,
                '&' => second is '&' or '=' ? 2 : 1,
                '|' => second is '|' or '=' ? 2 : 1,
                ':' => second == ':' ? 2 : 1,
                '.' or '~' or '?' or ',' or ';' => 1,
                '(' or ')' or '[' or ']' or '{' or '}' => 1,
                _ => 0
            };
        }

        private void ReadInvalidCharacter()
        {
            char current = source[index];
            int length = char.IsHighSurrogate(current) && char.IsLowSurrogate(Peek(1)) ? 2 : 1;
            AddDiagnostic(
                InvalidCharacterCode,
                current == '\0'
                    ? "Source contains a NUL character."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Character U+{(int)current:X4} cannot begin a token."),
                line,
                column);
            for (int step = 0; step < length; step++)
            {
                Advance();
            }
        }

        // ------------------------------------------------------------ normalising

        /// <summary>Normalises <c>255,128,0</c>, <c>0xFF,0x80,0x00</c> or a named colour to decimal <c>r,g,b</c>.</summary>
        private static string? NormaliseColour(string content)
        {
            string trimmed = content.Trim();
            if (NamedColours.TryGetValue(trimmed, out string? named))
            {
                return named;
            }

            string[] parts = trimmed.Split(ComponentSeparators);
            if (parts.Length != 3)
            {
                return null;
            }

            var components = new int[3];
            for (int part = 0; part < 3; part++)
            {
                if (!TryParseComponent(parts[part].Trim(), out int component)
                    || component is < 0 or > 255)
                {
                    return null;
                }

                components[part] = component;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{components[0]},{components[1]},{components[2]}");
        }

        private static bool TryParseComponent(string text, out int component)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(
                    text.AsSpan(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out component);
            }

            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out component);
        }

        /// <summary>
        /// Normalises a date literal to <c>yyyy.MM.dd HH:mm:ss</c>. An empty literal (which MQL5
        /// resolves to the compilation date) normalises to the empty string; a time-only literal
        /// takes the MQL5 epoch date.
        /// </summary>
        private static string? NormaliseDateTime(string content)
        {
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            int year = 1970;
            int month = 1;
            int day = 1;
            int hour = 0;
            int minute = 0;
            int second = 0;
            bool sawDate = false;
            bool sawTime = false;
            foreach (string part in trimmed.Split(
                BlankSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Contains(':', StringComparison.Ordinal))
                {
                    if (sawTime || !TryParseTime(part, out hour, out minute, out second))
                    {
                        return null;
                    }

                    sawTime = true;
                    continue;
                }

                if (sawDate || !TryParseDate(part, out year, out month, out day))
                {
                    return null;
                }

                sawDate = true;
            }

            if (!sawDate && !sawTime)
            {
                return null;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{year:D4}.{month:D2}.{day:D2} {hour:D2}:{minute:D2}:{second:D2}");
        }

        /// <summary>
        /// Parses the date half of a date literal. MQL5 writes <c>yyyy.mm.dd</c>; sources that
        /// carry the legacy <c>dd.mm.yyyy</c> spelling are accepted too, because a four-digit
        /// trailing field can only be the year.
        /// </summary>
        private static bool TryParseDate(string part, out int year, out int month, out int day)
        {
            year = 1970;
            month = 1;
            day = 1;
            string[] fields = part.Split(DateSeparators);
            if (fields.Length != 3
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int first)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int last))
            {
                return false;
            }

            if (first >= 1000)
            {
                year = first;
                day = last;
            }
            else if (last >= 1000)
            {
                year = last;
                day = first;
            }
            else
            {
                return false;
            }

            return year is >= 1 and <= 9999
                && month is >= 1 and <= 12
                && day >= 1
                && day <= DateTime.DaysInMonth(year, month);
        }

        private static bool TryParseTime(string part, out int hour, out int minute, out int second)
        {
            hour = 0;
            minute = 0;
            second = 0;
            string[] fields = part.Split(TimeSeparators);
            if (fields.Length is < 2 or > 3
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            {
                return false;
            }

            if (fields.Length == 3
                && !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out second))
            {
                return false;
            }

            return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 && second is >= 0 and <= 59;
        }

        // ----------------------------------------------------------------- cursor

        private char Peek(int offset)
        {
            int target = index + offset;
            return target < source.Length ? source[target] : '\0';
        }

        /// <summary>
        /// Consumes one character, or a whole <c>\r\n</c> pair, keeping the 1-based line and
        /// column current. Columns are counted in UTF-16 code units.
        /// </summary>
        private void Advance()
        {
            char current = source[index];
            index++;
            if (current == '\r')
            {
                if (index < source.Length && source[index] == '\n')
                {
                    index++;
                }

                line++;
                column = 1;
                atLineStart = true;
                return;
            }

            if (current == '\n')
            {
                line++;
                column = 1;
                atLineStart = true;
                return;
            }

            column++;
        }

        private static bool IsNewLine(char value) => value is '\n' or '\r';

        private static bool IsIdentifierStart(char value) =>
            value == '_' || char.IsLetter(value);

        private static bool IsIdentifierPart(char value) =>
            value == '_' || char.IsLetterOrDigit(value);

        private static bool IsPunctuator(char value) =>
            value is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';' or ':';

        private void AddToken(
            Mql5TokenKind kind,
            string text,
            string? value,
            int tokenLine,
            int tokenColumn,
            int position)
        {
            tokens.Add(new(kind, text, value, tokenLine, tokenColumn, position));
            lastTokenEndLine = line;
            atLineStart = false;
        }

        private void AddDiagnostic(string code, string message, int diagnosticLine, int diagnosticColumn)
        {
            if (diagnostics.Count >= MaximumDiagnostics)
            {
                if (!diagnosticLimitReported)
                {
                    diagnosticLimitReported = true;
                    diagnostics.Add(new(
                        LimitExceededCode,
                        Mql5RestrictedDiagnosticSeverity.Error,
                        "Lexical diagnostic limit reached; further diagnostics are suppressed.",
                        diagnosticLine,
                        diagnosticColumn));
                }

                return;
            }

            diagnostics.Add(new(
                code,
                Mql5RestrictedDiagnosticSeverity.Error,
                message,
                diagnosticLine,
                diagnosticColumn));
        }
    }
}
