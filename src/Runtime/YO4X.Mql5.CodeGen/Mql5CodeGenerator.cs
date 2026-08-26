using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// Translates a bound MQL5 module into C# source text.
///
/// The pass is fail-closed per construct. Anything it cannot translate produces an
/// error diagnostic carrying the original source line and makes the whole result
/// unsuccessful; the construct is replaced in the emitted text by an undefined
/// identifier, so that source which failed generation can never be compiled by
/// accident. It never emits a partial method body and never drops a statement,
/// because a strategy that compiles with a missing branch is worse than one that
/// refuses to compile at all.
///
/// The emitter is a pure function of the module and its semantic model: no clock,
/// no environment, no unordered iteration. The same module always produces
/// byte-identical output.
/// </summary>
public static class Mql5CodeGenerator
{
    /// <summary>
    /// Generates C# for <paramref name="module"/> using <paramref name="model"/> for
    /// name resolution and expression types.
    /// </summary>
    public static Mql5CodeGenResult Generate(Mql5IrV2Module module, Mql5SemanticModel model)
    {
        if (module is null)
        {
            return new Mql5CodeGenResult(
                false,
                null,
                "SStrategy",
                [Diagnostic(Mql5CodeGenDiagnosticCodes.InternalFailure, "No module was supplied.", 1, 1)]);
        }

        if (model is null || !ReferenceEquals(model.Module, module))
        {
            Mql5BindResult bound = Mql5Binder.Bind(module);
            model = bound.Model;
        }

        try
        {
            var run = new Mql5GeneratorRun(module, model);
            return run.Execute();
        }
#pragma warning disable CA1031 // Generation is an analysis pass and must never propagate a failure.
        catch (Exception error)
#pragma warning restore CA1031
        {
            return new Mql5CodeGenResult(
                false,
                null,
                Mql5ClrTypes.TypeNameFromPath(module.SourcePath),
                [
                    Diagnostic(
                        Mql5CodeGenDiagnosticCodes.InternalFailure,
                        "Generation stopped early: " + error.GetType().Name + ".",
                        1,
                        1),
                ]);
        }
    }

    private static Mql5RestrictedDiagnostic Diagnostic(string code, string message, int line, int column) =>
        new(code, Mql5RestrictedDiagnosticSeverity.Error, message, line, column);
}
