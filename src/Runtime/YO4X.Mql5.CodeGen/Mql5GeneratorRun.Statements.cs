using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// Statement emission. Every one of the thirteen IR statement forms is handled here;
/// the default arm is a diagnostic, never a silent skip, so a form added to the IR
/// later cannot slip through as a dropped statement.
/// </summary>
internal sealed partial class Mql5GeneratorRun
{
    private void EmitBlock(Mql5IrBlockStatement block, int depth)
    {
        _writer.EndLineDirectives();
        _writer.OpenBrace();
        foreach (Mql5IrStatement statement in block.Statements)
        {
            EmitStatement(statement, depth + 1);
        }

        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    /// <summary>Emits a statement, always braced when it is a loop or branch body.</summary>
    private void EmitBody(Mql5IrStatement statement, int depth)
    {
        if (statement is Mql5IrBlockStatement block)
        {
            EmitBlock(block, depth);
            return;
        }

        _writer.EndLineDirectives();
        _writer.OpenBrace();
        EmitStatement(statement, depth + 1);
        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    private void EmitStatement(Mql5IrStatement statement, int depth)
    {
        if (!Budget(depth, statement.Line, statement.Column))
        {
            _writer.Line(PoisonToken + ";");
            return;
        }

        switch (statement)
        {
            case Mql5IrBlockStatement block:
                EmitBlock(block, depth);
                break;
            case Mql5IrLocalDeclarationStatement declaration:
                EmitLocalDeclaration(declaration, depth);
                break;
            case Mql5IrExpressionStatement expression:
                EmitExpressionStatement(expression, depth);
                break;
            case Mql5IrEmptyStatement empty:
                _writer.LineDirective(empty.Line);
                _writer.Line("{ }");
                break;
            case Mql5IrIfStatement conditional:
                EmitIf(conditional, depth);
                break;
            case Mql5IrWhileStatement loop:
                _writer.LineDirective(loop.Line);
                _writer.Line("while (" + Truthy(loop.Condition, depth + 1) + ")");
                EmitBody(loop.Body, depth);
                break;
            case Mql5IrDoWhileStatement loop:
                _writer.LineDirective(loop.Line);
                _writer.Line("do");
                EmitBody(loop.Body, depth);
                _writer.LineDirective(loop.Condition.Line);
                _writer.Line("while (" + Truthy(loop.Condition, depth + 1) + ");");
                break;
            case Mql5IrForStatement loop:
                EmitFor(loop, depth);
                break;
            case Mql5IrSwitchStatement selection:
                EmitSwitch(selection, depth);
                break;
            case Mql5IrReturnStatement returned:
                EmitReturn(returned, depth);
                break;
            case Mql5IrBreakStatement stop:
                _writer.LineDirective(stop.Line);
                _writer.Line("break;");
                break;
            case Mql5IrContinueStatement next:
                _writer.LineDirective(next.Line);
                _writer.Line("continue;");
                break;
            case Mql5IrDeleteStatement removal:
                _writer.LineDirective(removal.Line);
                _writer.Line("Mql5Ops.Delete(" + Expr(removal.Operand, depth + 1) + ");");
                break;
            default:
                _writer.LineDirective(statement.Line);
                _writer.Line(
                    Fail(
                        Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedStatement,
                        "The statement form '" + statement.Kind + "' is not translated.",
                        statement.Line,
                        statement.Column)
                    + ";");
                break;
        }
    }

    private void EmitIf(Mql5IrIfStatement conditional, int depth)
    {
        _writer.LineDirective(conditional.Line);
        _writer.Line("if (" + Truthy(conditional.Condition, depth + 1) + ")");
        EmitBody(conditional.WhenTrue, depth);
        if (conditional.WhenFalse is null)
        {
            return;
        }

        _writer.EndLineDirectives();
        _writer.Line("else");
        EmitBody(conditional.WhenFalse, depth);
    }

    private void EmitLocalDeclaration(Mql5IrLocalDeclarationStatement declaration, int depth)
    {
        foreach (Mql5IrVariable variable in declaration.Variables)
        {
            _writer.LineDirective(variable.Line);

            if (declaration.IsStatic)
            {
                // The value lives in a hoisted field; the declaration site emits nothing,
                // because MQL5 initialises a static local exactly once.
                _writer.Line("// static " + variable.Name + " is hoisted to a strategy field.");
                continue;
            }

            _writer.Line(LocalDeclarationText(declaration, variable, depth));
        }
    }

    private string LocalDeclarationText(
        Mql5IrLocalDeclarationStatement declaration,
        Mql5IrVariable variable,
        int depth)
    {
        RefuseReservedName(variable.Name, variable.Line, variable.Column);

        string typeText = TypeText(declaration.Type, variable.ArrayRanks);
        string identifier = LocalName(variable.Name, variable.Line, variable.Column);
        Mql5ResolvedType target = ResolveWrittenType(declaration.Type, variable.ArrayRanks);

        string initializer;
        if (variable.Initializer is not null)
        {
            initializer = ValueText(declaration.Type, variable.ArrayRanks, variable.Initializer, depth + 1);
        }
        else if (target.IsArray)
        {
            string? core = CoreTypeName(declaration.Type);
            initializer = core is null
                ? PoisonToken
                : ArrayCreation(core, declaration.Type, variable.ArrayRanks, depth);
        }
        else if (target.Kind is Mql5ResolvedTypeKind.Structure or Mql5ResolvedTypeKind.Class
            && !declaration.Type.IsPointer)
        {
            // MQL5 gives an uninitialised structure zeroed storage, so the C# form must
            // construct one rather than leave a null the strategy would dereference.
            initializer = "new " + typeText + "(" + ConstructionArguments(typeText) + ")"
                + ConstructionInitializer(typeText);
        }
        else
        {
            initializer = Mql5ClrTypes.DefaultFor(declaration.Type.Scalar);
            if (declaration.Type.IsPointer || target.Kind == Mql5ResolvedTypeKind.Class)
            {
                initializer = "null";
            }
        }

        return typeText + " " + identifier + " = " + initializer + ";";
    }

    private void EmitExpressionStatement(Mql5IrExpressionStatement statement, int depth)
    {
        _writer.LineDirective(statement.Line);
        string text = Expr(statement.Expression, depth + 1);

        bool standsAlone = statement.Expression switch
        {
            Mql5IrCallExpression => true,
            Mql5IrAssignmentExpression => true,
            Mql5IrNewExpression => true,
            Mql5IrUnaryExpression unary => unary.Operator is "++" or "--" or "delete",
            _ => false
        };

        // Expressions are emitted fully parenthesised so precedence can never shift.
        // As a statement that is invalid C# — `(x = y);` does not parse — so the one
        // redundant outer pair is removed here.
        _writer.Line(standsAlone ? StripOuterParentheses(text) + ";" : "_ = " + text + ";");
    }

    /// <summary>
    /// Removes a single outermost balanced parenthesis pair. A string whose leading
    /// '(' does not match its trailing ')' — such as <c>(a) + (b)</c> — is returned
    /// unchanged.
    /// </summary>
    private static string StripOuterParentheses(string text)
    {
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')')
        {
            return text;
        }

        int depth = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index == text.Length - 1 ? text[1..^1] : text;
                }
            }
        }

        return text;
    }

    private void EmitReturn(Mql5IrReturnStatement statement, int depth)
    {
        _writer.LineDirective(statement.Line);
        if (statement.Value is null || _currentReturnType.Scalar == Mql5IrScalarKind.Void)
        {
            _writer.Line("return;");
            return;
        }

        _writer.Line(
            "return "
            + ConvertTo(_currentReturnType, TypeOf(statement.Value), Expr(statement.Value, depth + 1))
            + ";");
    }

    /// <summary>
    /// A C# <c>for</c> header accepts only a declaration or a statement-expression list,
    /// so anything else in the initialiser position is refused rather than rewritten
    /// into a <c>while</c> — a rewrite would move the increment away from
    /// <c>continue</c> and quietly change the loop.
    /// </summary>
    private void EmitFor(Mql5IrForStatement loop, int depth)
    {
        string initializer = string.Empty;
        switch (loop.Initializer)
        {
            case null:
                break;
            case Mql5IrEmptyStatement:
                break;
            case Mql5IrLocalDeclarationStatement declaration when !declaration.IsStatic:
                initializer = ForDeclarationText(declaration, depth);
                break;
            case Mql5IrExpressionStatement expression:
                initializer = StripOuterParentheses(Expr(expression.Expression, depth + 1));
                break;
            default:
                initializer = Fail(
                    Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedStatement,
                    "A for-initialiser of form '" + loop.Initializer.Kind + "' is not translated.",
                    loop.Initializer.Line,
                    loop.Initializer.Column);
                break;
        }

        string condition = loop.Condition is null ? string.Empty : Truthy(loop.Condition, depth + 1);
        string increment = loop.Increment is null
            ? string.Empty
            : StripOuterParentheses(Expr(loop.Increment, depth + 1));

        _writer.LineDirective(loop.Line);
        _writer.Line("for (" + initializer + "; " + condition + "; " + increment + ")");
        EmitBody(loop.Body, depth);
    }

    private string ForDeclarationText(Mql5IrLocalDeclarationStatement declaration, int depth)
    {
        string typeText = TypeText(declaration.Type, []);
        var parts = new List<string>(declaration.Variables.Count);
        foreach (Mql5IrVariable variable in declaration.Variables)
        {
            if (variable.ArrayRanks.Count != 0)
            {
                parts.Add(
                    Fail(
                        Mql5CodeGenDeclarationDiagnosticCodes.UnsupportedStatement,
                        "An array declared in a for-initialiser is not translated.",
                        variable.Line,
                        variable.Column));
                continue;
            }

            Mql5ResolvedType target = ResolveWrittenType(declaration.Type, []);
            string value = variable.Initializer is null
                ? Mql5ClrTypes.DefaultFor(declaration.Type.Scalar)
                : ConvertTo(target, TypeOf(variable.Initializer), Expr(variable.Initializer, depth + 1));
            parts.Add(LocalName(variable.Name, variable.Line, variable.Column) + " = " + value);
        }

        return typeText + " " + string.Join(", ", parts);
    }

    /// <summary>
    /// C# forbids one section falling into the next, but it does provide
    /// <c>goto case</c>, which means the same thing exactly. MQL5 fallthrough is
    /// therefore translated rather than refused — closing the section with a
    /// <c>break</c> instead would have silently changed which branch runs.
    /// </summary>
    private void EmitSwitch(Mql5IrSwitchStatement selection, int depth)
    {
        bool wide = TypeOf(selection.Subject).Kind == Mql5ResolvedTypeKind.Enumeration
            || selection.Sections.Any(
                section => section.Labels.Any(
                    label => label.Value is not null
                        && TypeOf(label.Value).Kind == Mql5ResolvedTypeKind.Enumeration));

        _writer.LineDirective(selection.Line);
        string subject = Expr(selection.Subject, depth + 1);
        _writer.Line("switch (" + (wide ? "(long)(" + subject + ")" : subject) + ")");
        _writer.EndLineDirectives();
        _writer.OpenBrace();

        for (int position = 0; position < selection.Sections.Count; position++)
        {
            Mql5IrSwitchSection section = selection.Sections[position];
            foreach (Mql5IrSwitchLabel label in section.Labels)
            {
                _writer.Line(LabelText(label, wide, depth) + ":");
            }

            if (section.Statements.Count == 0)
            {
                continue;
            }

            bool terminated = section.Statements[^1]
                is Mql5IrBreakStatement or Mql5IrContinueStatement or Mql5IrReturnStatement;

            _writer.EndLineDirectives();
            _writer.OpenBrace();
            foreach (Mql5IrStatement statement in section.Statements)
            {
                EmitStatement(statement, depth + 1);
            }

            if (!terminated)
            {
                _writer.EndLineDirectives();
                _writer.Line(FallthroughText(selection, position, wide, depth, section));
            }

            _writer.EndLineDirectives();
            _writer.CloseBrace();
        }

        _writer.EndLineDirectives();
        _writer.CloseBrace();
    }

    private string LabelText(Mql5IrSwitchLabel label, bool wide, int depth)
    {
        if (label.IsDefault || label.Value is null)
        {
            return "default";
        }

        string value = Expr(label.Value, depth + 1);
        return "case " + (wide ? "(long)(" + value + ")" : value);
    }

    /// <summary>
    /// The jump that reproduces MQL5 fallthrough. Falling out of the final section is
    /// the same as leaving the switch, so that one becomes a plain <c>break</c>.
    /// </summary>
    private string FallthroughText(
        Mql5IrSwitchStatement selection,
        int position,
        bool wide,
        int depth,
        Mql5IrSwitchSection section)
    {
        if (position + 1 >= selection.Sections.Count)
        {
            return "break;";
        }

        Mql5IrSwitchSection next = selection.Sections[position + 1];
        if (next.Labels.Count == 0)
        {
            return Fail(
                Mql5CodeGenDiagnosticCodes.UnsupportedSwitchFallthrough,
                "The switch section falls through into a section with no label.",
                section.Line,
                section.Column) + ";";
        }

        return "goto " + LabelText(next.Labels[0], wide, depth) + ";";
    }
}
