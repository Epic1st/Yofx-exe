using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// Finds the locals a function declares more than once in nested scopes, and picks a distinct C#
/// name for each of the inner ones.
/// </summary>
/// <remarks>
/// <para>
/// MQL5 lets an inner block redeclare a name an enclosing block already declared; C# does not, and
/// reports the inner declaration. Renaming is the only faithful translation available — the two
/// declarations really are different variables, and MQL5 code relies on that.
/// </para>
/// <para>
/// The rule C# applies is narrower than "the name is already in use". A conflict exists only when
/// one declaration's block encloses the other's; two sibling blocks may each declare
/// <c>elapsed</c> and that is legal C#. It is also not decidable while walking forward: an outer
/// declaration written <em>after</em> an inner one still conflicts with it, because a C# local's
/// scope is its whole enclosing block rather than the part after its declaration. Both facts force
/// a pre-pass over the function body before a single statement is emitted.
/// </para>
/// <para>
/// Renamed locals are keyed by declaration position rather than by name. Two locals called
/// <c>elapsed</c> need two different answers, so a name-keyed map — which is what the neighbouring
/// static-local map uses — would give the same one to both.
/// </para>
/// </remarks>
internal static class Mql5ShadowedLocals
{
    /// <summary>
    /// Maps the declaration position of every shadowing local to the name it is emitted under.
    /// Positions absent from the map keep their source spelling.
    /// </summary>
    public static IReadOnlyDictionary<(int Line, int Column), string> Resolve(Mql5IrFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);

        var renamed = new Dictionary<(int, int), string>();
        if (function.Body is null)
        {
            return renamed;
        }

        // The parameter list is the outermost frame: a local in the body that repeats a parameter
        // name is shadowing just as surely as one that repeats an enclosing local.
        var outermost = new List<Declaration>(function.Parameters.Count);
        foreach (Mql5IrParameter parameter in function.Parameters)
        {
            outermost.Add(new Declaration(parameter.Name, parameter.Line, parameter.Column));
        }

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        Walk(function.Body, [outermost], renamed, counters, 0);
        return renamed;
    }

    private sealed record Declaration(string Name, int Line, int Column);

    /// <summary>
    /// Walks one statement with <paramref name="enclosing"/> holding the declarations of every
    /// block that encloses it, outermost first.
    /// </summary>
    private static void Walk(
        Mql5IrStatement statement,
        List<List<Declaration>> enclosing,
        Dictionary<(int, int), string> renamed,
        Dictionary<string, int> counters,
        int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (statement)
        {
            case Mql5IrBlockStatement block:
                WalkBlock(block.Statements, enclosing, renamed, counters, depth);
                return;

            case Mql5IrLocalDeclarationStatement declaration when !declaration.IsStatic:
                foreach (Mql5IrVariable variable in declaration.Variables)
                {
                    Declare(variable.Name, variable.Line, variable.Column, enclosing, renamed, counters);
                }

                return;

            case Mql5IrIfStatement conditional:
                // A branch is its own scope even when it is a single statement, because a
                // declaration there cannot be seen from the other branch.
                WalkBlock([conditional.WhenTrue], enclosing, renamed, counters, depth);
                if (conditional.WhenFalse is not null)
                {
                    WalkBlock([conditional.WhenFalse], enclosing, renamed, counters, depth);
                }

                return;

            case Mql5IrWhileStatement loop:
                WalkBlock([loop.Body], enclosing, renamed, counters, depth);
                return;

            case Mql5IrDoWhileStatement loop:
                WalkBlock([loop.Body], enclosing, renamed, counters, depth);
                return;

            case Mql5IrForStatement loop:
                // The initialiser shares a scope with the body: `for(int i=…) { int i; }` is a
                // conflict in C#, so both are walked in one frame.
                WalkBlock(
                    loop.Initializer is null ? [loop.Body] : [loop.Initializer, loop.Body],
                    enclosing,
                    renamed,
                    counters,
                    depth);
                return;

            case Mql5IrSwitchStatement selection:
                // Every section of a C# switch shares one scope, so they are walked together.
                var sectionBodies = new List<Mql5IrStatement>();
                foreach (Mql5IrSwitchSection section in selection.Sections)
                {
                    sectionBodies.AddRange(section.Statements);
                }

                WalkBlock(sectionBodies, enclosing, renamed, counters, depth);
                return;

            default:
                return;
        }
    }

    /// <summary>Walks the statements of one new scope, pushed onto <paramref name="enclosing"/>.</summary>
    private static void WalkBlock(
        IReadOnlyList<Mql5IrStatement> statements,
        List<List<Declaration>> enclosing,
        Dictionary<(int, int), string> renamed,
        Dictionary<string, int> counters,
        int depth)
    {
        var frame = new List<Declaration>();
        enclosing.Add(frame);

        // Declarations are collected for the whole block before its nested statements are walked,
        // because a C# local is in scope across its entire block and not merely after its own
        // declaration — so a nested block conflicts with a sibling declared later.
        foreach (Mql5IrStatement statement in statements)
        {
            if (statement is Mql5IrLocalDeclarationStatement declaration && !declaration.IsStatic)
            {
                foreach (Mql5IrVariable variable in declaration.Variables)
                {
                    Declare(variable.Name, variable.Line, variable.Column, enclosing, renamed, counters);
                }
            }
        }

        foreach (Mql5IrStatement statement in statements)
        {
            if (statement is not Mql5IrLocalDeclarationStatement)
            {
                Walk(statement, enclosing, renamed, counters, depth + 1);
            }
        }

        enclosing.RemoveAt(enclosing.Count - 1);
    }

    /// <summary>
    /// Records one declaration in the innermost frame, renaming it when an enclosing frame already
    /// declares the name.
    /// </summary>
    private static void Declare(
        string name,
        int line,
        int column,
        List<List<Declaration>> enclosing,
        Dictionary<(int, int), string> renamed,
        Dictionary<string, int> counters)
    {
        bool shadows = false;
        for (int frame = 0; frame < enclosing.Count - 1; frame++)
        {
            foreach (Declaration declared in enclosing[frame])
            {
                if (string.Equals(declared.Name, name, StringComparison.Ordinal))
                {
                    shadows = true;
                    break;
                }
            }

            if (shadows)
            {
                break;
            }
        }

        if (shadows)
        {
            // The counter is per name so that three nestings of the same name stay distinct, and it
            // is allocated in source order so the emitted names are reproducible.
            int ordinal = counters.GetValueOrDefault(name) + 1;
            counters[name] = ordinal;
            renamed[(line, column)] = Mql5ClrTypes.ShadowName(name, ordinal);
        }

        enclosing[^1].Add(new Declaration(name, line, column));
    }
}
