using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.StrategyGovernance;

/// <summary>
/// Compiles the explicitly supported, data-only MQL5 subset into canonical IR.
/// It never loads or executes source or native code. Unknown syntax fails closed.
/// </summary>
public static class Mql5RestrictedSubsetCompiler
{
    public const string SchemaVersion = "yo4x.mql5.restricted-ir.v1";
    public const string CompilerVersion = "yo4x-mql5-restricted-subset-compiler.v1";
    private const int MaximumSourceBytes = 16 * 1024 * 1024;
    private const int MaximumTokens = 2_000_000;

    private static readonly HashSet<string> ScalarTypes = new(
        ["bool", "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "string", "datetime", "color"],
        StringComparer.Ordinal);

    public static Mql5RestrictedCompilation Compile(Mql5SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Content);
        if (document.Content.Length > MaximumSourceBytes)
        {
            return Failure("SOURCE_SIZE_LIMIT_EXCEEDED", "Source exceeds the 16 MiB restricted compiler limit.", 1, 1);
        }

        Mql5DecodedSource decoded = Mql5SourceDecoder.Decode(document.Content);
        if (decoded.ContentKind != Mql5SourceContentKind.Text)
        {
            return Failure("SOURCE_NOT_TEXT", "Restricted compilation accepts decoded text only.", 1, 1);
        }

        List<Mql5RestrictedDiagnostic> diagnostics = [];
        List<Token> tokens = Lex(decoded.Text, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new(false, null, diagnostics);
        }

        var parser = new Parser(tokens, diagnostics);
        parser.Parse();
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == Mql5RestrictedDiagnosticSeverity.Error))
        {
            return new(false, null, diagnostics);
        }

        string sourceHash = Convert.ToHexStringLower(SHA256.HashData(document.Content));
        string payload = SerializeCanonical(sourceHash, parser.Structures, parser.Enums, parser.Inputs);
        string irHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string canonical = SerializeCanonical(sourceHash, parser.Structures, parser.Enums, parser.Inputs, irHash);
        var ir = new Mql5RestrictedIr(
            SchemaVersion,
            sourceHash,
            irHash,
            parser.Structures,
            parser.Enums,
            parser.Inputs,
            canonical);
        diagnostics.Add(new(
            "RESTRICTED_IR_LOWERING_PASSED",
            Mql5RestrictedDiagnosticSeverity.Information,
            "The complete translation unit was type-checked and lowered into the data-only restricted IR.",
            1,
            1));
        return new(true, ir, diagnostics);
    }

    private static string SerializeCanonical(
        string sourceHash,
        IReadOnlyList<Mql5RestrictedStructure> structures,
        IReadOnlyList<Mql5RestrictedEnumeration> enums,
        IReadOnlyList<Mql5RestrictedInput> inputs,
        string? irHash = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("sourceSha256", sourceHash);
            if (irHash is not null)
            {
                writer.WriteString("irSha256", irHash);
            }

            writer.WriteStartArray("structures");
            foreach (Mql5RestrictedStructure structure in structures)
            {
                writer.WriteStartObject();
                writer.WriteString("name", structure.Name);
                writer.WriteStartArray("fields");
                foreach (Mql5RestrictedField field in structure.Fields)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", field.Name);
                    writer.WriteString("type", field.Type);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("enums");
            foreach (Mql5RestrictedEnumeration item in enums)
            {
                writer.WriteStartObject();
                writer.WriteString("name", item.Name);
                writer.WriteStartArray("members");
                foreach (Mql5RestrictedEnumMember member in item.Members)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", member.Name);
                    writer.WriteNumber("value", member.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("inputs");
            foreach (Mql5RestrictedInput input in inputs)
            {
                writer.WriteStartObject();
                writer.WriteString("name", input.Name);
                writer.WriteString("type", input.Type);
                writer.WriteString("value", input.CanonicalValue);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<Token> Lex(string source, List<Mql5RestrictedDiagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        int index = 0;
        int line = 1;
        int column = 1;
        while (index < source.Length)
        {
            char current = source[index];
            if (char.IsWhiteSpace(current))
            {
                Advance(current, ref index, ref line, ref column);
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n') Advance(source[index], ref index, ref line, ref column);
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                int startLine = line;
                int startColumn = column;
                Advance('/', ref index, ref line, ref column);
                Advance('*', ref index, ref line, ref column);
                bool closed = false;
                while (index < source.Length)
                {
                    if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                    {
                        Advance('*', ref index, ref line, ref column);
                        Advance('/', ref index, ref line, ref column);
                        closed = true;
                        break;
                    }

                    Advance(source[index], ref index, ref line, ref column);
                }

                if (!closed) diagnostics.Add(new("UNTERMINATED_COMMENT", Mql5RestrictedDiagnosticSeverity.Error, "Block comment is not terminated.", startLine, startColumn));
                continue;
            }

            if (current == '#')
            {
                int startLine = line;
                int startColumn = column;
                int start = index;
                while (index < source.Length && source[index] != '\n') Advance(source[index], ref index, ref line, ref column);
                string directive = source[start..index].Trim();
                if (!(directive.StartsWith("#property", StringComparison.Ordinal)
                    || directive.StartsWith("#ifndef", StringComparison.Ordinal)
                    || directive.StartsWith("#define", StringComparison.Ordinal)
                    || directive.StartsWith("#endif", StringComparison.Ordinal)))
                {
                    diagnostics.Add(new("UNSUPPORTED_PREPROCESSOR_DIRECTIVE", Mql5RestrictedDiagnosticSeverity.Error, "Only property and include-guard directives are supported.", startLine, startColumn));
                }

                continue;
            }

            int tokenLine = line;
            int tokenColumn = column;
            if (char.IsLetter(current) || current == '_')
            {
                int start = index;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) Advance(source[index], ref index, ref line, ref column);
                tokens.Add(new(TokenKind.Identifier, source[start..index], tokenLine, tokenColumn));
            }
            else if (char.IsDigit(current) || (current == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                int start = index;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '.' or '+' or '-'))
                {
                    char value = source[index];
                    if ((value is '+' or '-') && index > start && source[index - 1] is not ('e' or 'E')) break;
                    Advance(value, ref index, ref line, ref column);
                }

                tokens.Add(new(TokenKind.Literal, source[start..index], tokenLine, tokenColumn));
            }
            else if (current == '"')
            {
                int start = index;
                Advance(current, ref index, ref line, ref column);
                bool escaped = false;
                bool closed = false;
                while (index < source.Length)
                {
                    char value = source[index];
                    Advance(value, ref index, ref line, ref column);
                    if (!escaped && value == '"')
                    {
                        closed = true;
                        break;
                    }

                    escaped = !escaped && value == '\\';
                    if (value != '\\') escaped = false;
                }

                if (!closed) diagnostics.Add(new("UNTERMINATED_STRING", Mql5RestrictedDiagnosticSeverity.Error, "String literal is not terminated.", tokenLine, tokenColumn));
                else tokens.Add(new(TokenKind.String, source[start..index], tokenLine, tokenColumn));
            }
            else if ("{};,=[]-".Contains(current, StringComparison.Ordinal))
            {
                tokens.Add(new(TokenKind.Symbol, current.ToString(CultureInfo.InvariantCulture), tokenLine, tokenColumn));
                Advance(current, ref index, ref line, ref column);
            }
            else
            {
                diagnostics.Add(new("UNSUPPORTED_TOKEN", Mql5RestrictedDiagnosticSeverity.Error, $"Token '{current}' is outside the restricted subset.", tokenLine, tokenColumn));
                Advance(current, ref index, ref line, ref column);
            }

            if (tokens.Count > MaximumTokens)
            {
                diagnostics.Add(new("TOKEN_LIMIT_EXCEEDED", Mql5RestrictedDiagnosticSeverity.Error, "Token limit exceeded.", line, column));
                break;
            }
        }

        tokens.Add(new(TokenKind.End, string.Empty, line, column));
        return tokens;
    }

    private static void Advance(char value, ref int index, ref int line, ref int column)
    {
        index++;
        if (value == '\n') { line++; column = 1; } else { column++; }
    }

    private static Mql5RestrictedCompilation Failure(string code, string message, int line, int column) =>
        new(false, null, [new(code, Mql5RestrictedDiagnosticSeverity.Error, message, line, column)]);

    private enum TokenKind { Identifier, Literal, String, Symbol, End }
    private sealed record Token(TokenKind Kind, string Text, int Line, int Column);

    private sealed class Parser(List<Token> tokens, List<Mql5RestrictedDiagnostic> diagnostics)
    {
        private readonly HashSet<string> names = new(StringComparer.Ordinal);
        private int position;
        public List<Mql5RestrictedStructure> Structures { get; } = [];
        public List<Mql5RestrictedEnumeration> Enums { get; } = [];
        public List<Mql5RestrictedInput> Inputs { get; } = [];

        public void Parse()
        {
            while (Current.Kind != TokenKind.End && diagnostics.Count == 0)
            {
                if (Match("struct")) ParseStruct();
                else if (Match("enum")) ParseEnum();
                else if (Match("input") || Match("sinput")) ParseInput();
                else Error("UNSUPPORTED_TOP_LEVEL_CONSTRUCT", "Only structures, enumerations, and input constants can be lowered.", Current);
            }
        }

        private void ParseStruct()
        {
            Token name = RequireIdentifier("EXPECTED_STRUCTURE_NAME");
            Register(name);
            Require("{");
            var fields = new List<Mql5RestrictedField>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            while (!At("}") && Current.Kind != TokenKind.End && diagnostics.Count == 0)
            {
                Token type = RequireType();
                Token field = RequireIdentifier("EXPECTED_FIELD_NAME");
                if (!fieldNames.Add(field.Text)) Error("DUPLICATE_FIELD", $"Field '{field.Text}' is duplicated.", field);
                if (Match("[")) Error("ARRAY_FIELD_NOT_SUPPORTED", "Array fields are not part of restricted IR v1.", Previous);
                Require(";");
                fields.Add(new(field.Text, type.Text));
            }

            Require("}");
            Match(";");
            Structures.Add(new(name.Text, fields));
        }

        private void ParseEnum()
        {
            Token name = RequireIdentifier("EXPECTED_ENUM_NAME");
            Register(name);
            Require("{");
            var members = new List<Mql5RestrictedEnumMember>();
            var memberNames = new HashSet<string>(StringComparer.Ordinal);
            long next = 0;
            while (!At("}") && Current.Kind != TokenKind.End && diagnostics.Count == 0)
            {
                Token member = RequireIdentifier("EXPECTED_ENUM_MEMBER");
                if (!memberNames.Add(member.Text)) Error("DUPLICATE_ENUM_MEMBER", $"Enum member '{member.Text}' is duplicated.", member);
                long value = next;
                if (Match("=")) value = ParseSignedInteger();
                members.Add(new(member.Text, value));
                try { next = checked(value + 1); } catch (OverflowException) { Error("ENUM_VALUE_OVERFLOW", "Enum auto-value overflows Int64.", member); }
                if (!Match(",")) break;
            }

            Require("}");
            Match(";");
            Enums.Add(new(name.Text, members));
        }

        private void ParseInput()
        {
            Token type = RequireType();
            Token name = RequireIdentifier("EXPECTED_INPUT_NAME");
            Register(name);
            Require("=");
            bool negative = Match("-");
            Token literal = Current;
            if (literal.Kind is not (TokenKind.Literal or TokenKind.String or TokenKind.Identifier))
            {
                Error("EXPECTED_CONSTANT_LITERAL", "Input defaults must be scalar literals.", literal);
                return;
            }

            position++;
            string canonical = CanonicalLiteral(type.Text, literal, negative);
            Require(";");
            Inputs.Add(new(name.Text, type.Text, canonical));
        }

        private string CanonicalLiteral(string type, Token literal, bool negative)
        {
            string text = negative ? "-" + literal.Text : literal.Text;
            if (type == "string")
            {
                if (negative || literal.Kind != TokenKind.String) Error("INPUT_TYPE_MISMATCH", "String input requires a string literal.", literal);
                try { return JsonSerializer.Deserialize<string>(literal.Text) ?? string.Empty; } catch (JsonException) { Error("INVALID_STRING_LITERAL", "String escape sequence is invalid.", literal); return string.Empty; }
            }

            if (type == "bool")
            {
                if (!negative && literal.Text is "true" or "false") return literal.Text;
                Error("INPUT_TYPE_MISMATCH", "Boolean input requires true or false.", literal);
                return string.Empty;
            }

            if (type is "float" or "double")
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)) return value.ToString("R", CultureInfo.InvariantCulture);
                Error("INVALID_NUMERIC_LITERAL", "Floating-point input is invalid or non-finite.", literal);
                return string.Empty;
            }

            if (literal.Kind != TokenKind.Literal
                || !BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger integer))
            {
                Error("INPUT_TYPE_MISMATCH", $"Input type '{type}' requires an integer literal in restricted IR v1.", literal);
                return string.Empty;
            }

            (BigInteger minimum, BigInteger maximum) = type switch
            {
                "char" => (sbyte.MinValue, sbyte.MaxValue),
                "uchar" => (byte.MinValue, byte.MaxValue),
                "short" => (short.MinValue, short.MaxValue),
                "ushort" => (ushort.MinValue, ushort.MaxValue),
                "int" => (int.MinValue, int.MaxValue),
                "uint" or "color" => (uint.MinValue, uint.MaxValue),
                "long" or "datetime" => (long.MinValue, long.MaxValue),
                "ulong" => (ulong.MinValue, ulong.MaxValue),
                _ => (BigInteger.One, BigInteger.Zero)
            };
            if (integer < minimum || integer > maximum)
            {
                Error("INTEGER_LITERAL_OUT_OF_RANGE", $"Integer input is outside the range of '{type}'.", literal);
                return string.Empty;
            }

            return integer.ToString(CultureInfo.InvariantCulture);
        }

        private long ParseSignedInteger()
        {
            bool negative = Match("-");
            Token value = Current;
            if (value.Kind != TokenKind.Literal || !long.TryParse((negative ? "-" : string.Empty) + value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                Error("INVALID_ENUM_VALUE", "Enum values must be signed decimal Int64 literals.", value);
                return 0;
            }

            position++;
            return parsed;
        }

        private Token RequireType()
        {
            Token token = RequireIdentifier("EXPECTED_TYPE_NAME");
            if (!ScalarTypes.Contains(token.Text)) Error("UNSUPPORTED_TYPE", $"Type '{token.Text}' is outside restricted IR v1.", token);
            return token;
        }

        private void Register(Token token)
        {
            if (!names.Add(token.Text)) Error("DUPLICATE_TOP_LEVEL_SYMBOL", $"Top-level symbol '{token.Text}' is duplicated.", token);
        }

        private Token RequireIdentifier(string code)
        {
            Token token = Current;
            if (token.Kind != TokenKind.Identifier) { Error(code, "Identifier expected.", token); return token; }
            position++;
            return token;
        }

        private void Require(string text)
        {
            if (!Match(text)) Error("EXPECTED_TOKEN", $"Expected '{text}'.", Current);
        }

        private bool Match(string text)
        {
            if (!At(text)) return false;
            position++;
            return true;
        }

        private bool At(string text) => string.Equals(Current.Text, text, StringComparison.Ordinal);
        private Token Current => tokens[Math.Min(position, tokens.Count - 1)];
        private Token Previous => tokens[Math.Max(0, position - 1)];
        private void Error(string code, string message, Token token) => diagnostics.Add(new(code, Mql5RestrictedDiagnosticSeverity.Error, message, token.Line, token.Column));
    }
}
