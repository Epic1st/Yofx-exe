using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// Expression emission, including the conversions that keep MQL5 semantics when they
/// differ from C#: truth of a non-boolean, arithmetic on a logical value, a datetime
/// measured in seconds, and <c>+</c> meaning concatenation once either side is text.
/// </summary>
internal sealed partial class Mql5GeneratorRun
{
    /// <summary>Emits one expression. All thirteen IR expression forms are handled.</summary>
    private string Expr(Mql5IrExpression expression, int depth)
    {
        if (!Budget(depth, expression.Line, expression.Column))
        {
            return PoisonToken;
        }

        return expression switch
        {
            Mql5IrLiteralExpression literal => EmitLiteral(literal),
            Mql5IrNameExpression name => EmitName(name),
            Mql5IrUnaryExpression unary => EmitUnary(unary, depth),
            Mql5IrBinaryExpression binary => EmitBinary(binary, depth),
            Mql5IrAssignmentExpression assignment => EmitAssignment(assignment, depth),
            Mql5IrConditionalExpression conditional => EmitConditional(conditional, depth),
            Mql5IrCallExpression call => EmitCall(call, depth),
            Mql5IrIndexExpression index => EmitIndex(index, depth),
            Mql5IrMemberExpression member => EmitMember(member, depth),
            Mql5IrCastExpression cast => EmitCast(cast, depth),
            Mql5IrTypeNameExpression typeName => EmitTypeName(typeName, depth),
            Mql5IrNewExpression creation => EmitNew(creation, depth),
            Mql5IrSizeOfExpression size => EmitSizeOf(size),
            Mql5IrInitializerListExpression list => Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedInitializer,
                "A brace initialiser appears where no array type is known.",
                list.Line,
                list.Column),
            _ => Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                "The expression form '" + expression.Kind + "' is not translated.",
                expression.Line,
                expression.Column)
        };
    }

    // ------------------------------------------------------------------ literals

    private string EmitLiteral(Mql5IrLiteralExpression literal)
    {
        switch (literal.LiteralKind)
        {
            case Mql5LiteralKind.Boolean:
                return literal.CanonicalText;
            case Mql5LiteralKind.Null:
                return "null";
            case Mql5LiteralKind.Whole:
                if (literal.FoldedValue is long folded)
                {
                    return folded is >= int.MinValue and <= int.MaxValue
                        ? folded.ToString(CultureInfo.InvariantCulture)
                        : folded.ToString(CultureInfo.InvariantCulture) + "L";
                }

                if (TryParseUnsigned(literal.Text, out ulong wide))
                {
                    return wide.ToString(CultureInfo.InvariantCulture) + "UL";
                }

                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                    "The integer literal '" + literal.Text + "' does not fit a CLR integral type.",
                    literal.Line,
                    literal.Column);
            case Mql5LiteralKind.Real:
                if (double.TryParse(
                        literal.CanonicalText, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
                    && double.IsFinite(real))
                {
                    return real.ToString("R", CultureInfo.InvariantCulture) + "D";
                }

                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                    "The real literal '" + literal.Text + "' is not a finite double.",
                    literal.Line,
                    literal.Column);
            case Mql5LiteralKind.Text:
                return TryDecodeText(literal.Text, out string decoded)
                    ? CSharpStringLiteral(decoded)
                    : Fail(
                        Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                        "The string literal could not be decoded.",
                        literal.Line,
                        literal.Column);
            case Mql5LiteralKind.Character:
                return TryDecodeCharacter(literal.Text, out int code)
                    ? "(ushort)" + code.ToString(CultureInfo.InvariantCulture)
                    : Fail(
                        Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                        "The character literal '" + literal.Text + "' could not be decoded.",
                        literal.Line,
                        literal.Column);
            case Mql5LiteralKind.Colour:
                return TryDecodeColour(literal.Text, out int colour)
                    ? colour.ToString(CultureInfo.InvariantCulture)
                    : Fail(
                        Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                        "The colour literal '" + literal.Text + "' could not be decoded.",
                        literal.Line,
                        literal.Column);
            case Mql5LiteralKind.DateTime:
                return TryDecodeMoment(literal.Text, out long seconds)
                    ? seconds.ToString(CultureInfo.InvariantCulture) + "L"
                    : Fail(
                        Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                        "The datetime literal '" + literal.Text + "' could not be decoded.",
                        literal.Line,
                        literal.Column);
            default:
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedLiteral,
                    "The literal kind '" + Mql5IrLiteral.KindToken(literal.LiteralKind) + "' is not translated.",
                    literal.Line,
                    literal.Column);
        }
    }

    private static bool TryParseUnsigned(string text, out ulong value)
    {
        string candidate = text.Trim();
        while (candidate.Length > 0 && candidate[^1] is 'u' or 'U' or 'l' or 'L')
        {
            candidate = candidate[..^1];
        }

        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                candidate[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Decodes an MQL5 string lexeme, escapes and all, into the characters it denotes.
    /// Re-encoding is done separately, because C# and MQL5 do not spell every escape
    /// the same way and passing the lexeme through unchanged would corrupt some of them.
    /// </summary>
    private static bool TryDecodeText(string lexeme, out string value)
    {
        value = string.Empty;
        if (lexeme.Length < 2 || lexeme[0] != '"' || lexeme[^1] != '"')
        {
            return false;
        }

        var builder = new StringBuilder(lexeme.Length);
        int index = 1;
        int end = lexeme.Length - 1;
        while (index < end)
        {
            char character = lexeme[index++];
            if (character != '\\' || index >= end)
            {
                builder.Append(character);
                continue;
            }

            if (!TryDecodeEscape(lexeme, end, ref index, out char decoded))
            {
                return false;
            }

            builder.Append(decoded);
        }

        value = builder.ToString();
        return true;
    }

    private static bool TryDecodeEscape(string lexeme, int end, ref int index, out char decoded)
    {
        char escape = lexeme[index++];
        switch (escape)
        {
            case 'n': decoded = '\n'; return true;
            case 't': decoded = '\t'; return true;
            case 'r': decoded = '\r'; return true;
            case 'a': decoded = '\a'; return true;
            case 'b': decoded = '\b'; return true;
            case 'f': decoded = '\f'; return true;
            case 'v': decoded = '\v'; return true;
            case '0': decoded = '\0'; return true;
            case '\\': decoded = '\\'; return true;
            case '"': decoded = '"'; return true;
            case '\'': decoded = '\''; return true;
            case 'x':
            case 'u':
                int value = 0;
                int digits = 0;
                while (index < end && digits < 4 && Uri.IsHexDigit(lexeme[index]))
                {
                    value = (value * 16) + Convert.ToInt32(lexeme[index].ToString(), 16);
                    index++;
                    digits++;
                }

                decoded = (char)value;
                return digits != 0;
            default:
                decoded = escape;
                return true;
        }
    }

    private static string CSharpStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (character < ' ' || character == (char)0x7F)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static bool TryDecodeCharacter(string lexeme, out int code)
    {
        code = 0;
        if (lexeme.Length < 3 || lexeme[0] != '\'' || lexeme[^1] != '\'')
        {
            return false;
        }

        int index = 1;
        int end = lexeme.Length - 1;
        char character = lexeme[index++];
        if (character == '\\' && index < end)
        {
            if (!TryDecodeEscape(lexeme, end, ref index, out char decoded))
            {
                return false;
            }

            code = decoded;
            return true;
        }

        code = character;
        return true;
    }

    /// <summary>
    /// Decodes <c>C'r,g,b'</c>. MQL5 stores a colour as <c>0x00BBGGRR</c>, so the blue
    /// component is the high byte; the ordering is the whole point of decoding it here
    /// rather than passing the lexeme along.
    /// </summary>
    private static bool TryDecodeColour(string lexeme, out int value)
    {
        value = 0;
        int open = lexeme.IndexOf('\'', StringComparison.Ordinal);
        int close = lexeme.LastIndexOf('\'');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string[] parts = lexeme[(open + 1)..close].Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        int packed = 0;
        for (int index = 0; index < 3; index++)
        {
            string part = parts[index].Trim();
            bool parsed = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int component)
                : int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out component);
            if (!parsed || component is < 0 or > 255)
            {
                return false;
            }

            packed |= component << (8 * index);
        }

        value = packed;
        return true;
    }

    /// <summary>Decodes <c>D'YYYY.MM.DD HH:MI:SS'</c> into seconds since the MQL5 epoch.</summary>
    private static bool TryDecodeMoment(string lexeme, out long seconds)
    {
        seconds = 0;
        int open = lexeme.IndexOf('\'', StringComparison.Ordinal);
        int close = lexeme.LastIndexOf('\'');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string body = lexeme[(open + 1)..close].Trim();
        if (body.Length == 0)
        {
            return false;
        }

        // MQL5 accepts '.', '/' and '-' interchangeably, allows unpadded fields, and reads the
        // date as dd.mm.yyyy when the first field is not a four-digit year — `D'01.01.1970'` is
        // the epoch, not the first of January in year 1. The two orders cannot collide, because
        // `yyyy` demands four digits and `M` cannot exceed twelve, so listing both is unambiguous.
        string[] formats =
        [
            "yyyy.M.d H:mm:ss", "yyyy.M.d H:mm", "yyyy.M.d",
            "yyyy/M/d H:mm:ss", "yyyy/M/d H:mm", "yyyy/M/d",
            "yyyy-M-d H:mm:ss", "yyyy-M-d H:mm", "yyyy-M-d",
            "d.M.yyyy H:mm:ss", "d.M.yyyy H:mm", "d.M.yyyy",
            "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy",
            "d-M-yyyy H:mm:ss", "d-M-yyyy H:mm", "d-M-yyyy",
            "H:mm:ss", "H:mm"
        ];
        if (!DateTime.TryParseExact(
                body,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
        {
            return false;
        }

        seconds = (long)(parsed - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        return true;
    }

    // --------------------------------------------------------------------- names

    private string EmitName(Mql5IrNameExpression name)
    {
        if (name.Scope.Count != 0)
        {
            return EmitQualifiedName(name);
        }

        if (Mql5ClrTypes.PredefinedVariables.TryGetValue(name.Name, out string? predefined))
        {
            // A predefined variable compiles to a runtime call, and a module type carries its own
            // runtime reference, so this is reachable from inside a type body as well.
            return Mql5RuntimeContract.RuntimeFieldName + "." + predefined;
        }

        if (_staticLocalNames.TryGetValue(name.Name, out string? hoisted))
        {
            return hoisted;
        }

        if (_currentEnumName is not null)
        {
            // Inside an enumeration body a sibling member must be spelled bare: C#
            // requires the initialiser to have the underlying type, not the enum type.
            return Mql5ClrTypes.Identifier(name.Name);
        }

        Mql5ResolvedSymbol? symbol = _model.SymbolOf(name);
        switch (symbol?.Kind)
        {
            case Mql5SymbolKind.LocalVariable:
                return LocalName(name.Name, symbol.DeclarationLine, symbol.DeclarationColumn);
            case Mql5SymbolKind.Parameter:
            case Mql5SymbolKind.Field:
                return Mql5ClrTypes.Identifier(name.Name);
            case Mql5SymbolKind.GlobalVariable:
            case Mql5SymbolKind.Input:
                return FileScopeReference(name.Name);
            case Mql5SymbolKind.EnumMember:
                return EmitEnumMember(name);
            case Mql5SymbolKind.BuiltinConstant:
                return ConstantReference(name.Name);
            case Mql5SymbolKind.Define:
                return EmitDefine(name);
            case Mql5SymbolKind.TypeName:
            case Mql5SymbolKind.EnumerationName:
            case Mql5SymbolKind.BuiltinType:
                return EmitTypeAsName(name);
            case Mql5SymbolKind.Function:
            case Mql5SymbolKind.Method:
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedPointer,
                    "The function '" + name.Name + "' is used as a value, which needs a function pointer.",
                    name.Line,
                    name.Column);
            default:
                return EmitUnboundName(name);
        }
    }

    private string EmitUnboundName(Mql5IrNameExpression name)
    {
        if (_enumMemberOwner.ContainsKey(name.Name))
        {
            return EmitEnumMember(name);
        }

        if (_defines.ContainsKey(name.Name))
        {
            return EmitDefine(name);
        }

        if (string.Equals(name.Name, "NULL", StringComparison.Ordinal))
        {
            return "null";
        }

        if (Mql5BuiltinConstants.IsKnown(name.Name)
            || Mql5BuiltinRealConstants.IsKnown(name.Name)
            || Mql5ClrTypes.IsPredefinedConstant(name.Name)
            || ColourConstantFor(name.Name) is not null)
        {
            return ConstantReference(name.Name);
        }

        if (_fileScopeVariables.Contains(name.Name))
        {
            return FileScopeReference(name.Name);
        }

        if (_typeNames.ContainsKey(name.Name) || _enumTypeNames.ContainsKey(name.Name))
        {
            return EmitTypeAsName(name);
        }

        return Fail(
            Mql5CodeGenDiagnosticCodes.UnresolvedName,
            "The name '" + name.Name + "' resolved to nothing that can be emitted.",
            name.Line,
            name.Column);
    }

    private string EmitEnumMember(Mql5IrNameExpression name)
    {
        if (_enumMemberOwner.TryGetValue(name.Name, out string? owner))
        {
            if (owner is null)
            {
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnresolvedName,
                    "The enumeration member '" + name.Name + "' is declared by more than one enumeration.",
                    name.Line,
                    name.Column);
            }

            return owner + "." + Mql5ClrTypes.Identifier(name.Name);
        }

        return ConstantReference(name.Name);
    }

    private string EmitTypeAsName(Mql5IrNameExpression name)
    {
        if (_typeNames.TryGetValue(name.Name, out string? declared))
        {
            return declared;
        }

        if (_enumTypeNames.TryGetValue(name.Name, out string? enumeration))
        {
            return enumeration;
        }

        if (Mql5ClrTypes.RuntimeTypeNames.Contains(name.Name))
        {
            return Mql5ClrTypes.Identifier(Mql5ClrTypes.RuntimeTypeName(name.Name));
        }

        // A built-in MQL5 enumeration used in expression position is a cast or a conversion
        // spelled as a call. It is an int-sized integer type, exactly as in a declaration, so it
        // is spelled the same way here; see CoreTypeName for why int rather than a C# enum.
        if (name.Name.StartsWith("ENUM_", StringComparison.Ordinal)
            || Mql5BuiltinConstants.EnumNames.Contains(name.Name))
        {
            return "int";
        }

        return Fail(
            Mql5CodeGenDiagnosticCodes.UnsupportedType,
            "The type name '" + name.Name + "' maps onto no CLR type.",
            name.Line,
            name.Column);
    }

    /// <summary>
    /// A <c>#define</c> is substituted only when its replacement is a single literal or
    /// a single named constant. Anything longer is arbitrary token soup whose meaning
    /// depends on where it is pasted, and C# would read some of it differently, so it
    /// is refused rather than substituted.
    /// </summary>
    private string EmitDefine(Mql5IrNameExpression name)
    {
        if (!_defines.TryGetValue(name.Name, out string? replacement))
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedDefine,
                "The replacement for '" + name.Name + "' is unknown.",
                name.Line,
                name.Column);
        }

        string? substituted = TrySubstituteDefine(replacement, 0)
            ?? TrySubstituteDefineExpression(replacement, 0);
        if (substituted is not null)
        {
            return substituted;
        }

        return Fail(
            Mql5CodeGenDiagnosticCodes.UnsupportedDefine,
            "The replacement for '" + name.Name + "' could not be substituted as an expression.",
            name.Line,
            name.Column);
    }


    /// <summary>
    /// Substitutes a macro whose replacement is a compound expression rather than one atom.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An MQL5 <c>#define</c> is textual substitution, so its replacement is whatever expression
    /// the author wrote: <c>#define LABEL_X (PANEL_X+PAD)</c> composes two other macros, and
    /// <c>#define Bars __Mql4Bars()</c> forwards to a compatibility shim the file declares.
    /// Accepting only a single literal or constant refused both, and those two shapes account for
    /// every macro in the corpus that the single-atom path could not take.
    /// </para>
    /// <para>
    /// The rewrite is conservative on purpose. Operators, parentheses, separators and numbers pass
    /// through unchanged; every identifier must resolve to something nameable, and anything else in
    /// the text — a character this method does not recognise — refuses the whole macro. A macro is
    /// invisible in the generated source, so a substitution that is merely plausible would be a
    /// silent change of meaning with nothing left to read.
    /// </para>
    /// </remarks>
    private string? TrySubstituteDefineExpression(string text, int depth)
    {
        if (depth > 8 || text.Length == 0)
        {
            return null;
        }

        var rewritten = new StringBuilder(text.Length + 16);
        int index = 0;

        while (index < text.Length)
        {
            char character = text[index];

            if (char.IsWhiteSpace(character))
            {
                rewritten.Append(' ');
                index++;
                continue;
            }

            if (IsIdentifierStart(character))
            {
                int start = index;
                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }

                string atom = text[start..index];

                // A name immediately followed by '(' is a call, and only a module function this
                // file declares can be one; a built-in would need argument conversion this textual
                // path cannot perform.
                int lookahead = index;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < text.Length && text[lookahead] == '(')
                {
                    if (!_functions.ContainsKey(atom))
                    {
                        return null;
                    }

                    rewritten.Append(Mql5ClrTypes.Identifier(atom));
                    continue;
                }

                string? resolved = TrySubstituteDefine(atom, depth + 1) ?? TrySubstituteDefineName(atom);
                if (resolved is null)
                {
                    return null;
                }

                rewritten.Append(resolved);
                continue;
            }

            if (char.IsAsciiDigit(character) || character == '.')
            {
                int start = index;
                while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '.'))
                {
                    index++;
                }

                rewritten.Append(text[start..index]);
                continue;
            }

            if (!DefineOperatorCharacters.Contains(character))
            {
                return null;
            }

            rewritten.Append(character);
            index++;
        }

        string result = rewritten.ToString().Trim();
        return result.Length == 0 ? null : "(" + result + ")";
    }

    /// <summary>A macro atom that names file-scope state rather than a constant.</summary>
    private string? TrySubstituteDefineName(string atom)
    {
        if (_insideTypeBody)
        {
            // The name would compile to a field reference that a nested type cannot reach, which
            // FailOuterScope reports properly at the use site rather than here.
            return null;
        }

        return _fileScopeVariables.Contains(atom) ? Mql5ClrTypes.Identifier(atom) : null;
    }

    /// <summary>
    /// The characters a macro replacement may carry outside identifiers and numbers.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes quotes: a string or character literal inside a compound replacement
    /// would need escaping this rewrite does not perform, and the single-atom path already handles
    /// a replacement that is nothing but a literal.
    /// </remarks>
    private static readonly FrozenSet<char> DefineOperatorCharacters =
        "+-*/%()[],<>=!&|^~?:".ToFrozenSet();

    private static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '_';

    private string? TrySubstituteDefine(string replacement, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        string text = replacement.Trim();
        while (text.Length > 2 && text[0] == '(' && text[^1] == ')' && IsBalancedInside(text))
        {
            text = text[1..^1].Trim();
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (string.Equals(text, "true", StringComparison.Ordinal)
            || string.Equals(text, "false", StringComparison.Ordinal))
        {
            return text;
        }

        if (string.Equals(text, "NULL", StringComparison.Ordinal))
        {
            return "null";
        }

        if (text[0] == '"' && text[^1] == '"' && TryDecodeText(text, out string decoded))
        {
            return CSharpStringLiteral(decoded);
        }

        if ((text[0] is 'C' or 'c') && text.Length > 2 && text[1] == '\'' && TryDecodeColour(text, out int colour))
        {
            return colour.ToString(CultureInfo.InvariantCulture);
        }

        if ((text[0] is 'D' or 'd') && text.Length > 2 && text[1] == '\''
            && TryDecodeMoment(text, out long moment))
        {
            return moment.ToString(CultureInfo.InvariantCulture) + "L";
        }

        if (Mql5IrLiteral.TryFoldWhole(text) is long whole)
        {
            return whole is >= int.MinValue and <= int.MaxValue
                ? whole.ToString(CultureInfo.InvariantCulture)
                : whole.ToString(CultureInfo.InvariantCulture) + "L";
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
            && double.IsFinite(real))
        {
            return real.ToString("R", CultureInfo.InvariantCulture) + "D";
        }

        if (!IsIdentifier(text))
        {
            return null;
        }

        if (_defines.TryGetValue(text, out string? chained))
        {
            return TrySubstituteDefine(chained, depth + 1);
        }

        if (_enumMemberOwner.TryGetValue(text, out string? owner) && owner is not null)
        {
            return owner + "." + Mql5ClrTypes.Identifier(text);
        }

        if (Mql5BuiltinConstants.IsKnown(text)
            || Mql5BuiltinRealConstants.IsKnown(text)
            || Mql5ClrTypes.IsPredefinedConstant(text)
            || ColourConstantFor(text) is not null)
        {
            return ConstantReference(text);
        }

        return null;
    }

    private static bool IsBalancedInside(string text)
    {
        int depth = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '(')
            {
                depth++;
            }
            else if (text[index] == ')')
            {
                depth--;
                if (depth == 0 && index != text.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || (!char.IsAsciiLetter(text[0]) && text[0] != '_'))
        {
            return false;
        }

        foreach (char character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private string EmitQualifiedName(Mql5IrNameExpression name)
    {
        string root = name.Scope[0];
        string? mapped = _typeNames.TryGetValue(root, out string? declared)
            ? declared
            : _enumTypeNames.TryGetValue(root, out string? enumeration)
                ? enumeration
                : Mql5ClrTypes.RuntimeTypeNames.Contains(root) || root.StartsWith("ENUM_", StringComparison.Ordinal)
                    ? Mql5ClrTypes.Identifier(root)
                    : null;

        if (mapped is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnresolvedName,
                "The scope qualifier '" + root + "' maps onto no CLR type.",
                name.Line,
                name.Column);
        }

        var parts = new List<string> { mapped };
        for (int index = 1; index < name.Scope.Count; index++)
        {
            parts.Add(Mql5ClrTypes.Identifier(name.Scope[index]));
        }

        parts.Add(Mql5ClrTypes.Identifier(name.Name));
        return string.Join(".", parts);
    }

    /// <summary>
    /// A read of file-scope state — a global or an input — from wherever it appears.
    /// </summary>
    /// <remarks>
    /// At file scope this is the field itself. Inside a module type it is the same field reached
    /// through the owner the construction site bound, because MQL5 gives a type's methods the same
    /// program scope the rest of the file has and C# gives a separate declaration none.
    /// </remarks>
    private string FileScopeReference(string name) =>
        (_insideTypeBody ? OwnerFieldName + "." : string.Empty) + Mql5ClrTypes.Identifier(name);

    private string FailOuterScope(string name, int line, int column) =>
        Fail(
            Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedOuterScopeReference,
            "'" + name + "' is file-scope state that a module type's method cannot reach in C#.",
            line,
            column);

    // ---------------------------------------------------------------- operators

    /// <summary>
    /// The type of an expression: the binder's answer when it has one, and a fallback
    /// inference when it does not.
    ///
    /// The binder leaves the result of a built-in call unresolved, which matters more
    /// than it sounds: without a type, <c>TimeCurrent() % 60</c> loses the knowledge
    /// that the left side is a datetime and emits <c>DateTime % 60</c>, which is not
    /// C#. The catalog already states every documented return type, so it is read here
    /// rather than guessed.
    /// </summary>
    private Mql5ResolvedType TypeOf(Mql5IrExpression expression)
    {
        Mql5ResolvedType type = _model.TypeOf(expression);
        return type.IsResolved ? type : Infer(expression, 0);
    }

    private Mql5ResolvedType Infer(Mql5IrExpression expression, int depth)
    {
        if (depth > 24)
        {
            return Mql5ResolvedType.Unknown;
        }

        switch (expression)
        {
            case Mql5IrCastExpression cast:
                return ResolveWrittenType(cast.Type, []);
            case Mql5IrNewExpression creation:
                return ResolveWrittenType(creation.Type, []);
            case Mql5IrCallExpression call when call.Callee is Mql5IrNameExpression name && name.Scope.Count == 0:
                return InferCall(name, call.Arguments.Count);
            case Mql5IrBinaryExpression binary:
                return InferBinary(binary);
            case Mql5IrCallExpression member when member.Callee is Mql5IrMemberExpression target:
                return InferMemberCall(target, depth);
            case Mql5IrConditionalExpression conditional:
                return CommonType(TypeOf(conditional.WhenTrue), TypeOf(conditional.WhenFalse));
            case Mql5IrIndexExpression index:
                return Infer(index.Target, depth + 1).ElementType();
            default:
                return Mql5ResolvedType.Unknown;
        }
    }


    /// <summary>
    /// The type of a binary expression, under MQL5's usual arithmetic conversions.
    /// </summary>
    /// <remarks>
    /// Without this, a binary expression inferred as <c>Unknown</c> and every conversion around it
    /// was skipped — so <c>StringToInteger(a) * 60 + StringToInteger(b)</c> was assigned straight
    /// into an <c>int</c>, and a <c>long</c> compared against a <c>ulong</c> was emitted with
    /// neither side adjusted. Neither is a translation MQL5 would refuse; both are C# errors, and
    /// the second is the more dangerous shape because a comparison is where a silent widening would
    /// change a decision rather than fail.
    /// </remarks>

    /// <summary>
    /// The type of a call on a member of a runtime-provided type.
    /// </summary>
    /// <remarks>
    /// Only the standard library classes can be typed this way: their shapes are transcribed from
    /// the runtime. A method on a type the module itself declares is left untyped here, because the
    /// binder already resolved it and the emitted signature came from the same MQL5 declaration as
    /// the call — so no conversion is needed between them.
    /// </remarks>
    private Mql5ResolvedType InferMemberCall(Mql5IrMemberExpression member, int depth)
    {
        Mql5ResolvedType targetType = Infer(member.Target, depth + 1);
        if (targetType.Kind is not (Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class))
        {
            targetType = TypeOf(member.Target);
        }

        if (targetType.Kind is not (Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class))
        {
            return Mql5ResolvedType.Unknown;
        }

        string clrName = Mql5ClrTypes.RuntimeTypeName(targetType.Name);
        return Mql5ClrTypes.LibraryReturnType(clrName, member.Member) is string spelling
            ? ScalarFor(spelling)
            : Mql5ResolvedType.Unknown;
    }

    /// <summary>The resolved type for one of the spellings the runtime tables record.</summary>
    private static Mql5ResolvedType ScalarFor(string spelling) => spelling switch
    {
        "bool" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Logical),
        "sbyte" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole8),
        "byte" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural8),
        "short" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole16),
        "ushort" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural16),
        "int" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole32),
        "uint" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural32),
        "long" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Whole64),
        "ulong" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Natural64),
        "float" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real32),
        "double" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Real64),
        "string" => Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text),
        _ => Mql5ResolvedType.Unknown
    };

    private Mql5ResolvedType InferBinary(Mql5IrBinaryExpression binary)
    {
        switch (binary.Operator)
        {
            case "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||":
                return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Logical);

            // A shift takes the promoted type of its left operand; the right operand only says how
            // far, and MQL5 does not widen the result to accommodate it.
            case "<<" or ">>":
                return TypeOf(binary.Left);

            default:
                return CommonType(TypeOf(binary.Left), TypeOf(binary.Right));
        }
    }

    private Mql5ResolvedType InferCall(Mql5IrNameExpression name, int argumentCount)
    {
        if (Mql5ClrTypes.ScalarKeywords.TryGetValue(name.Name, out Mql5IrScalarKind scalar))
        {
            return Mql5ResolvedType.ForScalar(scalar);
        }

        if (_functions.TryGetValue(name.Name, out List<Mql5IrFunction>? overloads) && overloads.Count != 0)
        {
            return ResolveWrittenType(overloads[0].ReturnType, []);
        }

        if (!Mql5BuiltinCatalog.TryGet(name.Name, out IReadOnlyList<Mql5BuiltinSignature> signatures))
        {
            return Mql5ResolvedType.Unknown;
        }

        string? agreed = null;
        foreach (Mql5BuiltinSignature signature in signatures)
        {
            if (!signature.Verified || !signature.AcceptsArgumentCount(argumentCount))
            {
                continue;
            }

            if (agreed is null)
            {
                agreed = signature.ReturnTypeName;
            }
            else if (!string.Equals(agreed, signature.ReturnTypeName, StringComparison.Ordinal))
            {
                return Mql5ResolvedType.Unknown;
            }
        }

        return agreed is null ? Mql5ResolvedType.Unknown : ResolveCatalogTypeName(agreed);
    }

    /// <summary>Maps an MQL5 type name as the catalog spells it onto a resolved type.</summary>
    private static Mql5ResolvedType ResolveCatalogTypeName(string typeName)
    {
        string core = typeName.Trim();
        int rank = 0;
        while (core.EndsWith("[]", StringComparison.Ordinal))
        {
            core = core[..^2].TrimEnd();
            rank++;
        }

        if (core.StartsWith("const ", StringComparison.Ordinal))
        {
            core = core[6..].Trim();
        }

        if (Mql5ClrTypes.ScalarKeywords.TryGetValue(core, out Mql5IrScalarKind scalar))
        {
            return Mql5ResolvedType.ForScalar(scalar).WithArrayRank(rank);
        }

        if (core.StartsWith("ENUM_", StringComparison.Ordinal) || Mql5BuiltinConstants.EnumNames.Contains(core))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Enumeration, Mql5IrScalarKind.None, core, rank, false, true);
        }

        if (Mql5ClrTypes.RuntimeTypeNames.Contains(core))
        {
            return new Mql5ResolvedType(
                Mql5ResolvedTypeKind.Structure, Mql5IrScalarKind.None, core, rank, false, true);
        }

        return Mql5ResolvedType.Unknown;
    }

    /// <summary>An expression in a condition position, where MQL5 accepts any scalar.</summary>
    private string Truthy(Mql5IrExpression expression, int depth)
    {
        Mql5ResolvedType type = TypeOf(expression);
        string text = Expr(expression, depth);
        if (type.Scalar == Mql5IrScalarKind.Logical && !type.IsArray)
        {
            return text;
        }

        if (type.Kind == Mql5ResolvedTypeKind.Enumeration && !type.IsArray)
        {
            return "Mql5Ops.Truth((long)(" + text + "))";
        }

        return "Mql5Ops.Truth(" + text + ")";
    }

    /// <summary>An expression in an arithmetic position, with MQL5's promotions applied.</summary>
    /// <summary>
    /// Both operands of a binary expression, converted to the type MQL5's usual arithmetic
    /// conversions give them.
    /// </summary>
    /// <remarks>
    /// Promoting each side on its own is not enough. MQL5 brings the operands to a common type
    /// before comparing or combining them; C# does not, and between <c>long</c> and <c>ulong</c> it
    /// has no implicit conversion in either direction — so <c>ticket == PositionGetInteger(...)</c>
    /// is not a widening C# performs quietly, it is CS0034. Balancing here reproduces the MQL5 rule
    /// rather than leaving the pair mismatched.
    ///
    /// When the common type cannot be established each side is promoted on its own, which is the
    /// previous behaviour and still correct for every pair C# can reconcile itself.
    /// </remarks>
    private (string Left, string Right) Balanced(Mql5IrBinaryExpression binary, int depth)
    {
        Mql5ResolvedType left = TypeOf(binary.Left);
        Mql5ResolvedType right = TypeOf(binary.Right);
        Mql5ResolvedType common = CommonType(left, right);

        if (!common.IsResolved || common.IsArray || !common.IsArithmetic)
        {
            return (Arith(binary.Left, depth), Arith(binary.Right, depth));
        }

        return (
            ConvertTo(common, left, Expr(binary.Left, depth)),
            ConvertTo(common, right, Expr(binary.Right, depth)));
    }

    private string Arith(Mql5IrExpression expression, int depth) =>
        Promote(TypeOf(expression), Expr(expression, depth));

    private static string Promote(Mql5ResolvedType type, string text)
    {
        if (type.IsArray)
        {
            return text;
        }

        if (type.Scalar == Mql5IrScalarKind.Logical)
        {
            return "Mql5Ops.Num(" + text + ")";
        }

        if (type.Scalar == Mql5IrScalarKind.Moment)
        {
            return "Mql5Ops.Seconds(" + text + ")";
        }

        if (type.Kind == Mql5ResolvedTypeKind.Enumeration)
        {
            return "(long)(" + text + ")";
        }

        return text;
    }

    private string EmitUnary(Mql5IrUnaryExpression unary, int depth)
    {
        switch (unary.Operator)
        {
            case "!":
                return "(!" + Truthy(unary.Operand, depth + 1) + ")";
            case "~":
                return "(~" + Arith(unary.Operand, depth + 1) + ")";
            case "-":
            case "+":
                return "(" + unary.Operator + " " + Arith(unary.Operand, depth + 1) + ")";
            case "++":
            case "--":
                string operand = Expr(unary.Operand, depth + 1);
                return unary.IsPrefix
                    ? "(" + unary.Operator + operand + ")"
                    : "(" + operand + unary.Operator + ")";
            case "delete":
                // MQL5 `delete ptr` releases an object. The CLR collects, so the emitted helper
                // exists to give the expression a value and a place to record the release rather
                // than to free anything; refusing it would reject a construct MQL5 programs use
                // routinely for objects the garbage collector already handles.
                return "Mql5Ops.Delete(" + Expr(unary.Operand, depth + 1) + ")";
            case "*":
            case "&":
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedPointer,
                    "The pointer operator '" + unary.Operator + "' has no C# equivalent.",
                    unary.Line,
                    unary.Column);
            default:
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                    "The unary operator '" + unary.Operator + "' is not translated.",
                    unary.Line,
                    unary.Column);
        }
    }

    private string EmitBinary(Mql5IrBinaryExpression binary, int depth)
    {
        string op = binary.Operator;
        Mql5ResolvedType left = TypeOf(binary.Left);
        Mql5ResolvedType right = TypeOf(binary.Right);
        bool leftIsText = left.Scalar == Mql5IrScalarKind.Text && !left.IsArray;
        bool rightIsText = right.Scalar == Mql5IrScalarKind.Text && !right.IsArray;

        switch (op)
        {
            case "&&":
            case "||":
                return "(" + Truthy(binary.Left, depth + 1) + " " + op + " "
                    + Truthy(binary.Right, depth + 1) + ")";
            case "+" when leftIsText || rightIsText:
                return "Mql5Ops.Concat(" + Expr(binary.Left, depth + 1) + ", "
                    + Expr(binary.Right, depth + 1) + ")";
            case "<" or ">" or "<=" or ">=" when leftIsText && rightIsText:
                return "(string.CompareOrdinal(" + Expr(binary.Left, depth + 1) + ", "
                    + Expr(binary.Right, depth + 1) + ") " + op + " 0)";
            case "==" or "!=" when leftIsText != rightIsText:
                return "(Mql5Ops.ToText(" + Expr(binary.Left, depth + 1) + ") " + op
                    + " Mql5Ops.ToText(" + Expr(binary.Right, depth + 1) + "))";
            case "<<":
            case ">>":
                return "(" + Arith(binary.Left, depth + 1) + " " + op + " (int)("
                    + Arith(binary.Right, depth + 1) + "))";
            case "+":
            case "-":
            case "*":
            case "/":
            case "%":
            case "&":
            case "|":
            case "^":
            case "==":
            case "!=":
            case "<":
            case ">":
            case "<=":
            case ">=":
            {
                (string balancedLeft, string balancedRight) = Balanced(binary, depth + 1);
                return "(" + balancedLeft + " " + op + " " + balancedRight + ")";
            }
            default:
                return Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                    "The binary operator '" + op + "' is not translated.",
                    binary.Line,
                    binary.Column);
        }
    }

    /// <summary>
    /// MQL5 converts freely between integral widths; C# does not. An assignment into
    /// a runtime structure field whose CLR type is wider or unsigned gets the explicit
    /// conversion C# requires.
    /// </summary>
    private string WidenForRuntimeMember(Mql5IrExpression target, string value)
    {
        if (target is not Mql5IrMemberExpression member)
        {
            return value;
        }

        if (!_model.ExpressionTypes.TryGetValue(member.Target, out Mql5ResolvedType? targetType)
            || targetType is null
            || !targetType.IsBuiltin
            || targetType.Kind != Mql5ResolvedTypeKind.Structure)
        {
            return value;
        }

        string? clrType = Mql5ClrTypes.RuntimeMemberClrType(member.Member);
        return clrType is null ? value : "(" + clrType + ")(" + value + ")";
    }

    private string EmitAssignment(Mql5IrAssignmentExpression assignment, int depth)
    {
        Mql5ResolvedType target = TypeOf(assignment.Target);
        Mql5ResolvedType source = TypeOf(assignment.Value);
        string targetText = Expr(assignment.Target, depth + 1);

        if (string.Equals(assignment.Operator, "=", StringComparison.Ordinal))
        {
            string value = target.IsArray && assignment.Value is Mql5IrInitializerListExpression
                ? Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedInitializer,
                    "A brace initialiser can only appear in a declaration.",
                    assignment.Line,
                    assignment.Column)
                : ConvertTo(target, source, Expr(assignment.Value, depth + 1));
            return "(" + targetText + " = " + WidenForRuntimeMember(assignment.Target, value) + ")";
        }

        if (target.Scalar == Mql5IrScalarKind.Text && !target.IsArray)
        {
            if (string.Equals(assignment.Operator, "+=", StringComparison.Ordinal))
            {
                return "(" + targetText + " = Mql5Ops.Concat(" + targetText + ", "
                    + Expr(assignment.Value, depth + 1) + "))";
            }

            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                "The operator '" + assignment.Operator + "' has no meaning on text.",
                assignment.Line,
                assignment.Column);
        }

        if (target.Scalar == Mql5IrScalarKind.Moment && !target.IsArray)
        {
            if (assignment.Operator is "+=" or "-=")
            {
                // A datetime is a second count, so += and -= are ordinary integer arithmetic.
                return "(" + targetText + " " + assignment.Operator + " "
                    + Arith(assignment.Value, depth + 1) + ")";
            }

            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                "The operator '" + assignment.Operator + "' has no meaning on a datetime.",
                assignment.Line,
                assignment.Column);
        }

        if (target.Kind == Mql5ResolvedTypeKind.Enumeration && !target.IsArray)
        {
            string? clr = ClrOf(target);
            string core = assignment.Operator[..^1];
            return "(" + targetText + " = unchecked((" + (clr ?? PoisonToken) + ")((long)(" + targetText + ") "
                + core + " " + Arith(assignment.Value, depth + 1) + ")))";
        }

        if (target.Scalar == Mql5IrScalarKind.Logical && !target.IsArray
            && assignment.Operator is "|=" or "&=" or "^=" or "+=" or "-=" or "*=" or "/=" or "%=")
        {
            // MQL5 treats bool as an integer, so `found |= Check()` is ordinary arithmetic there.
            // C# has no such conversion: `|=` on a bool takes a bool, and the operands here are
            // whatever MQL5 was combining. The operation is performed on numbers and the result
            // converted back, which is what MQL5 does and what the declared type still requires.
            string core = assignment.Operator[..^1];
            return "(" + targetText + " = Mql5Ops.Truth(Mql5Ops.Num(" + targetText + ") "
                + core + " " + Arith(assignment.Value, depth + 1) + "))";
        }


        return "(" + targetText + " " + assignment.Operator + " " + Arith(assignment.Value, depth + 1) + ")";
    }

    private string EmitConditional(Mql5IrConditionalExpression conditional, int depth)
    {
        Mql5ResolvedType whenTrue = TypeOf(conditional.WhenTrue);
        Mql5ResolvedType whenFalse = TypeOf(conditional.WhenFalse);
        Mql5ResolvedType common = CommonType(whenTrue, whenFalse);

        return "(" + Truthy(conditional.Condition, depth + 1) + " ? "
            + ConvertTo(common, whenTrue, Expr(conditional.WhenTrue, depth + 1)) + " : "
            + ConvertTo(common, whenFalse, Expr(conditional.WhenFalse, depth + 1)) + ")";
    }

    private string EmitIndex(Mql5IrIndexExpression index, int depth)
    {
        Mql5ResolvedType target = TypeOf(index.Target);
        string targetText = Expr(index.Target, depth + 1);
        string indexText = "(int)(" + Arith(index.Index, depth + 1) + ")";

        if (target.Scalar == Mql5IrScalarKind.Text && !target.IsArray)
        {
            return "Mql5Ops.CharAt(" + targetText + ", " + indexText + ")";
        }

        return targetText + "[" + indexText + "]";
    }

    private string IndexText(Mql5IrExpression expression, int depth) =>
        "(int)(" + Arith(expression, depth) + ")";

    private string EmitMember(Mql5IrMemberExpression member, int depth)
    {
        string target = Expr(member.Target, depth + 1);
        string name = member.Member;

        // A field of a runtime-provided MQL5 structure is written in MQL5's
        // lower_snake_case; the runtime exposes it as a CLR property. Members of a
        // structure the module itself declared keep their written name.
        if (_model.ExpressionTypes.TryGetValue(member.Target, out Mql5ResolvedType? targetType)
            && targetType is not null
            && targetType.IsBuiltin
            && targetType.Kind == Mql5ResolvedTypeKind.Structure)
        {
            name = Mql5ClrTypes.RuntimeMemberName(name);
        }

        return target + "." + Mql5ClrTypes.Identifier(name);
    }


    /// <summary>
    /// MQL5 <c>typename</c>: the name of a type, as a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MetaQuotes folds this at compile time, and inside a template it folds per instantiation —
    /// the compiler reports <c>specified with [T=string]</c>. C# generics are not monomorphised, so
    /// there is no instantiation to fold against and the answer has to be computed at run time from
    /// the type argument. That is why this emits a call rather than a literal.
    /// </para>
    /// <para>
    /// The operand is spelled with <c>typeof</c> over its <em>static</em> type, never
    /// <c>GetType()</c> over its value: <c>typename</c> answers about the declaration, so a handle
    /// declared as a base and holding a derived instance must still name the base.
    /// </para>
    /// </remarks>
    private string EmitTypeName(Mql5IrTypeNameExpression typeName, int depth)
    {
        if (typeName.Type is Mql5IrTypeReference written)
        {
            string? core = CoreTypeName(written);
            return core is null
                ? Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedType,
                    "The type '" + written.Name + "' named by typename maps onto no CLR type.",
                    typeName.Line,
                    typeName.Column)
                : TypeNameCall(core, written.IsPointer);
        }

        if (typeName.Operand is not Mql5IrExpression operand)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedOperator,
                "A typename expression names neither a type nor a value.",
                typeName.Line,
                typeName.Column);
        }

        // A bare name that denotes a type is recorded by the binder as a TypeName symbol; that is
        // the `typename(T)` case, and the type parameter itself is what has to be named.
        if (operand is Mql5IrNameExpression name
            && _model.SymbolOf(name)?.Kind == Mql5SymbolKind.TypeName)
        {
            string? core = _typeParametersInScope.Contains(name.Name)
                ? Mql5ClrTypes.Identifier(name.Name)
                : CoreTypeName(new Mql5IrTypeReference(
                    name.Name,
                    Mql5IrScalarKind.None,
                    false,
                    false,
                    false,
                    [],
                    name.Line,
                    name.Column));

            return core is null
                ? Fail(
                    Mql5CodeGenDiagnosticCodes.UnsupportedType,
                    "The type '" + name.Name + "' named by typename maps onto no CLR type.",
                    typeName.Line,
                    typeName.Column)
                : TypeNameCall(core, isHandle: false);
        }

        Mql5ResolvedType resolved = TypeOf(operand);
        string? spelled = ClrOf(resolved);
        return spelled is null
            ? Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The type of the expression named by typename is not known to the emitter.",
                typeName.Line,
                typeName.Column)
            : TypeNameCall(spelled, isHandle: false);
    }

    private static string TypeNameCall(string clrType, bool isHandle) =>
        "Mql5TypeInfo.Mql5TypeName(typeof(" + clrType + ")"
            + (isHandle ? ", true)" : ")");

    private string EmitCast(Mql5IrCastExpression cast, int depth)
    {
        if (_typeParametersInScope.Contains(cast.Type.Name) && cast.Type.ArrayRanks.Count == 0)
        {
            // MQL5 lets a template body write `(T)0` and `(T)NULL` to mean "the zero of T",
            // whatever T turns out to be. C# rejects a cast from a literal to an unconstrained type
            // parameter, and `default(T)` is exactly the value MQL5 is asking for. Any other
            // operand is a genuine conversion this emitter cannot perform without knowing T, so it
            // is refused rather than guessed.
            if (IsZeroOrNullLiteral(cast.Operand))
            {
                return "default(" + Mql5ClrTypes.Identifier(cast.Type.Name) + ")";
            }

            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "A conversion to the type parameter '" + cast.Type.Name
                    + "' is only translated for a zero or null literal.",
                cast.Line,
                cast.Column);
        }

        Mql5ResolvedType target = ResolveWrittenType(cast.Type, []);
        if (target.Kind == Mql5ResolvedTypeKind.Unknown)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The cast target '" + cast.Type.Name + "' maps onto no CLR type.",
                cast.Line,
                cast.Column);
        }

        return Coerce(target, TypeOf(cast.Operand), Expr(cast.Operand, depth + 1), explicitCast: true);
    }

    /// <summary>Whether an expression is the literal zero, an empty string, or <c>NULL</c>.</summary>
    private static bool IsZeroOrNullLiteral(Mql5IrExpression expression) => expression switch
    {
        Mql5IrLiteralExpression literal =>
            literal.LiteralKind == Mql5LiteralKind.Null
            || (literal.LiteralKind == Mql5LiteralKind.Whole && literal.FoldedValue == 0L)
            || (literal.LiteralKind == Mql5LiteralKind.Text && literal.Text.Length <= 2),
        Mql5IrNameExpression name =>
            name.Scope.Count == 0 && string.Equals(name.Name, "NULL", StringComparison.Ordinal),
        _ => false
    };


    private string EmitNew(Mql5IrNewExpression creation, int depth)
    {
        string? core = CoreTypeName(creation.Type);
        if (core is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The type '" + creation.Type.Name + "' maps onto no CLR type.",
                creation.Line,
                creation.Column);
        }

        return creation.Type.ArrayRanks.Count == 0
            ? "new " + core + "(" + ConstructionArguments(core) + ")" + ConstructionInitializer(core)
            : ArrayCreation(core, creation.Type, [], depth);
    }

    /// <summary>
    /// MQL5 <c>sizeof</c>, over a type or over a variable.
    /// </summary>
    /// <remarks>
    /// A dynamic array measures 52 regardless of element type or rank — that is the descriptor,
    /// not the payload, and it was measured from the compiler for <c>char[]</c>, <c>double[]</c>,
    /// <c>MqlTick[]</c> and a two-rank array alike. A fixed array would measure its contents
    /// instead, but the resolved type records only a rank count, so the two are indistinguishable
    /// here. Every <c>sizeof</c> of an array in the corpus is of a dynamic one; a fixed array is
    /// refused rather than given the descriptor size, because silently reporting 52 for a
    /// <c>char[10]</c> would be wrong by a factor of five with nothing to notice it.
    /// </remarks>
    private string EmitSizeOf(Mql5IrSizeOfExpression size)
    {
        if (size.Operand is not null)
        {
            Mql5ResolvedType operandType = TypeOf(size.Operand);
            if (operandType.IsResolved && operandType.IsArray)
            {
                return "52";
            }

            if (operandType.IsResolved && Mql5ClrTypes.WidthOf(operandType.Scalar) is int operandWidth)
            {
                return operandWidth.ToString(CultureInfo.InvariantCulture);
            }
        }

        int? width = Mql5ClrTypes.WidthOf(size.Type.Scalar);
        return width is int bytes
            ? bytes.ToString(CultureInfo.InvariantCulture)
            : Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedSizeOf,
                "The layout of '" + size.Type.Name + "' is not known to the emitter.",
                size.Line,
                size.Column);
    }

    // --------------------------------------------------------------- conversions

    /// <summary>The CLR spelling of a resolved type, or null when it has none.</summary>
    /// <summary>
    /// The CLR spelling of an enumeration named in a cast or conversion.
    /// </summary>
    /// <remarks>
    /// A user-declared enumeration keeps its own emitted type. A built-in MQL5 enumeration is
    /// spelled <c>int</c>, the same as in a declaration and in expression position — the three
    /// sites have to agree, because a cast to a name no declaration emits produces C# that names
    /// a type which does not exist.
    /// </remarks>
    private string EnumerationClrName(string name)
    {
        if (_enumTypeNames.TryGetValue(name, out string? mapped))
        {
            return mapped;
        }

        return name.StartsWith("ENUM_", StringComparison.Ordinal)
            || Mql5BuiltinConstants.EnumNames.Contains(name)
                ? "int"
                : Mql5ClrTypes.Identifier(name);
    }

    /// <summary>
    /// A cast to a CLR numeric type, written the way MQL5 performs the conversion.
    /// </summary>
    /// <remarks>
    /// MQL5 narrows by truncating the value, silently and at runtime. C# agrees at runtime by
    /// default but rejects a <em>constant</em> that does not fit, so <c>char c = 251;</c> — legal
    /// MQL5, and how a great deal of code writes a byte pattern — fails to compile. Wrapping the
    /// cast in <c>unchecked</c> restores MQL5's rule for constants and leaves the runtime
    /// behaviour exactly as it was.
    /// </remarks>
    private static string NarrowingCast(string? clrType, string text) =>
        "unchecked((" + clrType + ")(" + text + "))";

    private string? ClrOf(Mql5ResolvedType type)
    {
        string? core = type.Kind switch
        {
            Mql5ResolvedTypeKind.Scalar => Mql5ClrTypes.Spell(type.Scalar),
            Mql5ResolvedTypeKind.Enumeration => EnumerationClrName(type.Name),
            Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class =>
                _typeNames.TryGetValue(type.Name, out string? declared)
                    ? declared
                    : Mql5ClrTypes.RuntimeTypeNames.Contains(type.Name)
                        ? Mql5ClrTypes.Identifier(Mql5ClrTypes.RuntimeTypeName(type.Name))
                        : Mql5ClrTypes.Identifier(type.Name),
            _ => null
        };

        if (core is null)
        {
            return null;
        }

        return type.ArrayRank switch
        {
            0 => core,
            1 => core + "[]",
            2 => core + "[][]",
            3 => core + "[][][]",
            _ => null
        };
    }

    /// <summary>An implicit MQL5 conversion, made explicit where C# requires it.</summary>
    private string ConvertTo(Mql5ResolvedType target, Mql5ResolvedType source, string text) =>
        Coerce(target, source, text, explicitCast: false);

    private string Coerce(Mql5ResolvedType target, Mql5ResolvedType source, string text, bool explicitCast)
    {
        if (!target.IsResolved || target.Scalar == Mql5IrScalarKind.Void || target.IsArray)
        {
            return text;
        }

        if (source.Kind == Mql5ResolvedTypeKind.NullLiteral)
        {
            return text;
        }

        if (!explicitCast && SameShape(target, source))
        {
            return text;
        }

        string? clr = ClrOf(target);
        if (clr is null)
        {
            return text;
        }

        if (target.Scalar == Mql5IrScalarKind.Text)
        {
            return source.Scalar == Mql5IrScalarKind.Text && !source.IsArray
                ? text
                : "Mql5Ops.ToText(" + text + ")";
        }

        if (target.Scalar == Mql5IrScalarKind.Moment)
        {
            return source.Scalar == Mql5IrScalarKind.Moment && !source.IsArray
                ? text
                : "(long)(" + Promote(source, text) + ")";
        }

        if (target.Scalar == Mql5IrScalarKind.Logical)
        {
            return source.Scalar == Mql5IrScalarKind.Logical && !source.IsArray
                ? text
                : "Mql5Ops.Truth(" + text + ")";
        }

        if (target.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class)
        {
            return explicitCast ? NarrowingCast(clr, text) : text;
        }

        if (target.Kind is Mql5ResolvedTypeKind.Scalar or Mql5ResolvedTypeKind.Enumeration)
        {
            if (source.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
                or Mql5ResolvedTypeKind.Unknown or Mql5ResolvedTypeKind.Function
                or Mql5ResolvedTypeKind.TypeName)
            {
                return explicitCast ? NarrowingCast(clr, text) : text;
            }

            // MQL5's conversion from a string to a number is a parse, not a reinterpretation:
            // `(int)"12"` is twelve. C# has no such conversion at all, so without this the cast is
            // emitted as a CLR cast from string and rejected outright.
            if (source.Scalar == Mql5IrScalarKind.Text && !source.IsArray)
            {
                string parsed = target.Scalar is Mql5IrScalarKind.Real32 or Mql5IrScalarKind.Real64
                    ? "Mql5Ops.ToDouble(" + text + ")"
                    : "Mql5Ops.ToLong(" + text + ")";
                return NarrowingCast(clr, parsed);
            }

            if (source.Scalar == Mql5IrScalarKind.Text && !explicitCast)
            {
                return text;
            }

            return NarrowingCast(clr, Promote(source, text));
        }

        return text;
    }

    private static bool SameShape(Mql5ResolvedType left, Mql5ResolvedType right) =>
        left.Kind == right.Kind
        && left.Scalar == right.Scalar
        && left.ArrayRank == right.ArrayRank
        && string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    /// <summary>
    /// The type both arms of a conditional are converted to. C# demands one; MQL5 does
    /// not, so the choice is made here rather than left to overload resolution.
    /// </summary>
    private static Mql5ResolvedType CommonType(Mql5ResolvedType left, Mql5ResolvedType right)
    {
        if (SameShape(left, right))
        {
            return left;
        }

        if (!left.IsResolved || !right.IsResolved || left.IsArray || right.IsArray)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (left.Kind == Mql5ResolvedTypeKind.NullLiteral || right.Kind == Mql5ResolvedTypeKind.NullLiteral)
        {
            return Mql5ResolvedType.Unknown;
        }

        if (left.Scalar == Mql5IrScalarKind.Text || right.Scalar == Mql5IrScalarKind.Text)
        {
            return Mql5ResolvedType.ForScalar(Mql5IrScalarKind.Text);
        }

        if (!left.IsArithmetic || !right.IsArithmetic)
        {
            return Mql5ResolvedType.Unknown;
        }

        return Mql5ResolvedType.ForScalar(WiderScalar(left.Scalar, right.Scalar));
    }

    private static Mql5IrScalarKind WiderScalar(Mql5IrScalarKind left, Mql5IrScalarKind right)
    {
        int leftRank = ScalarRank(left);
        int rightRank = ScalarRank(right);
        return leftRank >= rightRank ? Normalize(left) : Normalize(right);

        static Mql5IrScalarKind Normalize(Mql5IrScalarKind scalar) =>
            scalar == Mql5IrScalarKind.None ? Mql5IrScalarKind.Whole64 : scalar;
    }

    private static int ScalarRank(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Real64 => 10,
        Mql5IrScalarKind.Real32 => 9,
        Mql5IrScalarKind.Moment => 8,
        Mql5IrScalarKind.Natural64 => 7,
        Mql5IrScalarKind.Whole64 => 6,
        Mql5IrScalarKind.Natural32 => 5,
        Mql5IrScalarKind.Whole32 or Mql5IrScalarKind.Colour => 4,
        Mql5IrScalarKind.Natural16 => 3,
        Mql5IrScalarKind.Whole16 => 2,
        Mql5IrScalarKind.Natural8 => 1,
        _ => 0
    };

    /// <summary>A brace initialiser in a declaration whose type is an array.</summary>
    private string ArrayLiteral(Mql5IrTypeReference type, Mql5IrInitializerListExpression list, int depth)
    {
        string? core = CoreTypeName(type);
        if (core is null)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedType,
                "The element type '" + type.Name + "' maps onto no CLR type.",
                list.Line,
                list.Column);
        }

        return ArrayLiteralBody(core, ResolveWrittenType(type, []), list, depth);
    }

    private string ArrayLiteralBody(
        string core,
        Mql5ResolvedType element,
        Mql5IrInitializerListExpression list,
        int depth)
    {
        if (!Budget(depth, list.Line, list.Column))
        {
            return PoisonToken;
        }

        var items = new List<string>(list.Items.Count);
        bool nested = list.Items.Count != 0 && list.Items[0] is Mql5IrInitializerListExpression;
        foreach (Mql5IrExpression item in list.Items)
        {
            if (item is Mql5IrInitializerListExpression inner)
            {
                items.Add(ArrayLiteralBody(core, element, inner, depth + 1));
            }
            else
            {
                items.Add(ConvertTo(element, TypeOf(item), Expr(item, depth + 1)));
            }
        }

        string arrayType = nested ? core + "[][]" : core + "[]";
        return items.Count == 0
            ? "System.Array.Empty<" + (nested ? core + "[]" : core) + ">()"
            : "new " + arrayType + " { " + string.Join(", ", items) + " }";
    }
}
