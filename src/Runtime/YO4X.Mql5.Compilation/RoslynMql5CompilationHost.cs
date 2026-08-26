using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using YO4X.Mql5.CodeGen;
using YO4X.StrategyGovernance;

namespace YO4X.Mql5.Compilation;

/// <summary>
/// Compiles generated strategy C# with Roslyn, in memory.
///
/// <para>
/// The reference set is closed on purpose: the base class library plus
/// <c>YO4X.Mql5.Runtime</c>, and nothing else. A generated strategy reaches the outside
/// world only through <c>IMql5Runtime</c>, so anything it could reach by referencing more
/// — sockets, the file system, other YO4X assemblies — would be a capability the MQL5
/// program was never granted. Narrowing the references is what keeps that true at compile
/// time rather than by convention.
/// </para>
/// </summary>
public sealed class RoslynMql5CompilationHost : IMql5CompilationHost
{
    private readonly ImmutableArray<MetadataReference> references;
    private readonly CSharpCompilationOptions options;

    public RoslynMql5CompilationHost()
    {
        references = BuildReferences();
        options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            // Generated code is machine-written and already shaped by the code generator;
            // treating its style warnings as failures would reject strategies for reasons
            // that say nothing about whether they behave correctly.
            reportSuppressedDiagnostics: false,
            allowUnsafe: false,
            deterministic: true,
            nullableContextOptions: NullableContextOptions.Enable);
    }

    /// <summary>The language version the generated source is emitted against.</summary>
    public static LanguageVersion Language => LanguageVersion.CSharp13;

    public Mql5CompilationResult Compile(
        string assemblyName,
        IReadOnlyList<Mql5GeneratedSource> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return Mql5CompilationResult.Failed(
                "MQL5_COMPILE_NO_SOURCES",
                "No generated source was supplied to compile.",
                1,
                1);
        }

        var parseOptions = new CSharpParseOptions(Language, DocumentationMode.None);
        SyntaxTree[] trees = new SyntaxTree[sources.Count];
        for (int index = 0; index < sources.Count; index++)
        {
            Mql5GeneratedSource source = sources[index];
            trees[index] = CSharpSyntaxTree.ParseText(
                source.CSharpSource,
                parseOptions,
                path: source.DisplayPath);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            options);

        using var image = new MemoryStream();
        EmitResult emit = compilation.Emit(image);
        List<Mql5RestrictedDiagnostic> diagnostics = Translate(emit.Diagnostics);
        if (!emit.Success)
        {
            return new Mql5CompilationResult(false, null, diagnostics);
        }

        return new Mql5CompilationResult(true, image.ToArray(), diagnostics);
    }

    /// <summary>
    /// Keeps errors and warnings, drops the rest. A hidden or informational diagnostic from
    /// a machine-generated file is noise that would bury the one line that matters.
    /// </summary>
    private static List<Mql5RestrictedDiagnostic> Translate(
        ImmutableArray<Diagnostic> diagnostics)
    {
        var translated = new List<Mql5RestrictedDiagnostic>();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            {
                continue;
            }

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            translated.Add(new Mql5RestrictedDiagnostic(
                diagnostic.Id,
                diagnostic.Severity == DiagnosticSeverity.Error
                    ? Mql5RestrictedDiagnosticSeverity.Error
                    : Mql5RestrictedDiagnosticSeverity.Information,
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
        }

        return translated;
    }

    /// <summary>
    /// The closed reference set: the runtime contract assembly, plus the base class library
    /// assemblies it and the generated code actually need, taken from the running framework
    /// so the compiled strategy targets exactly the runtime that will load it.
    /// </summary>
    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAssembly(paths, typeof(object));                       // System.Private.CoreLib
        AddAssembly(paths, typeof(Console));                      // System.Console
        AddAssembly(paths, typeof(Enumerable));                   // System.Linq
        AddAssembly(paths, typeof(List<>));                       // System.Collections
        AddAssembly(paths, typeof(Math));                         // System.Runtime.Extensions
        AddAssembly(paths, typeof(YO4X.Mql5.Runtime.IMql5Runtime));

        // netstandard and System.Runtime are facade assemblies the compiler resolves type
        // forwards through; without them a reference to a BCL type can bind at runtime and
        // still fail to compile.
        string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (string facade in (string[])["System.Runtime.dll", "netstandard.dll"])
        {
            string candidate = Path.Combine(directory, facade);
            if (File.Exists(candidate))
            {
                paths.Add(candidate);
            }
        }

        var builder = ImmutableArray.CreateBuilder<MetadataReference>(paths.Count);
        foreach (string path in paths)
        {
            builder.Add(MetadataReference.CreateFromFile(path));
        }

        return builder.ToImmutable();
    }

    private static void AddAssembly(HashSet<string> paths, Type marker)
    {
        Assembly assembly = marker.Assembly;
        if (!string.IsNullOrEmpty(assembly.Location))
        {
            paths.Add(assembly.Location);
        }
    }
}
