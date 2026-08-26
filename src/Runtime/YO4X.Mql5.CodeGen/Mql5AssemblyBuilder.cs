using YO4X.StrategyGovernance;

namespace YO4X.Mql5.CodeGen;

/// <summary>
/// The result of asking a compilation host to turn generated C# into an assembly.
///
/// <paramref name="AssemblyBytes"/> is the raw image rather than a loaded assembly:
/// deciding where a strategy is loaded — a collectible context, a separate process,
/// not at all — is the host's call, and handing back an already-loaded assembly would
/// take that decision away.
/// </summary>
public sealed record Mql5CompilationResult(
    bool Succeeded,
    byte[]? AssemblyBytes,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics)
{
    /// <summary>A failed compilation carrying one diagnostic.</summary>
    public static Mql5CompilationResult Failed(string code, string message, int line, int column) =>
        new(
            false,
            null,
            [new Mql5RestrictedDiagnostic(code, Mql5RestrictedDiagnosticSeverity.Error, message, line, column)]);
}

/// <summary>
/// A C# compiler, supplied from outside this project.
///
/// This assembly deliberately carries no reference to Roslyn. Central package
/// management does not version <c>Microsoft.CodeAnalysis.CSharp</c>, and adding a
/// package version is not this project's decision to make, so the compiler arrives
/// through this interface instead. The valuable half of code generation is emitting
/// correct source; invoking a compiler over it is mechanical, and keeping it behind an
/// interface also lets a caller compile many strategies into one assembly, cache
/// compilations, or compile out of process.
/// </summary>
public interface IMql5CompilationHost
{
    /// <summary>
    /// Compiles <paramref name="sources"/> into one assembly image.
    /// </summary>
    /// <param name="assemblyName">The simple name to give the produced assembly.</param>
    /// <param name="sources">The generated compilation units, keyed by a display path.</param>
    Mql5CompilationResult Compile(string assemblyName, IReadOnlyList<Mql5GeneratedSource> sources);
}

/// <summary>One generated compilation unit and the strategy type it declares.</summary>
public sealed record Mql5GeneratedSource(string DisplayPath, string CSharpSource, string FullTypeName);

/// <summary>
/// Generation followed by compilation: the whole back half of the compiler, with the
/// compiler itself supplied by the caller.
/// </summary>
public static class Mql5AssemblyBuilder
{
    /// <summary>
    /// Generates C# for every module and, only if every one of them generated cleanly,
    /// asks <paramref name="host"/> to compile the lot.
    ///
    /// The all-or-nothing rule is deliberate. A batch where one strategy failed to
    /// generate must not produce an assembly that silently contains the others, because
    /// the caller would then hold a build that looks complete and is not.
    /// </summary>
    public static Mql5CompilationResult Build(
        string assemblyName,
        IReadOnlyList<Mql5IrV2Module> modules,
        IMql5CompilationHost host)
    {
        if (host is null)
        {
            return Mql5CompilationResult.Failed(
                Mql5CodeGenDiagnosticCodes.InternalFailure, "No compilation host was supplied.", 1, 1);
        }

        if (modules is null || modules.Count == 0)
        {
            return Mql5CompilationResult.Failed(
                Mql5CodeGenDiagnosticCodes.InternalFailure, "No modules were supplied.", 1, 1);
        }

        var diagnostics = new List<Mql5RestrictedDiagnostic>();
        var sources = new List<Mql5GeneratedSource>(modules.Count);
        bool generated = true;

        foreach (Mql5IrV2Module module in modules)
        {
            Mql5BindResult bound = Mql5Binder.Bind(module);
            Mql5CodeGenResult result = Mql5CodeGenerator.Generate(module, bound.Model);
            diagnostics.AddRange(result.Diagnostics);

            if (!result.Succeeded || result.CSharpSource is null)
            {
                generated = false;
                continue;
            }

            sources.Add(new Mql5GeneratedSource(module.SourcePath, result.CSharpSource, result.FullTypeName));
        }

        if (!generated)
        {
            return new Mql5CompilationResult(false, null, diagnostics);
        }

        Mql5CompilationResult compiled = host.Compile(assemblyName, sources);
        diagnostics.AddRange(compiled.Diagnostics);
        return compiled with { Diagnostics = diagnostics };
    }
}
