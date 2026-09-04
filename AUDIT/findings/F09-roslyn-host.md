---
agent_id: F09
lane: roslyn-host
scope:
  - src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs
status: COMPLETE
generated: 2026-08-29T08:25:00Z
counts: { P0: 1, P1: 1, P2: 2, P3: 1 }
---

# F09 — roslyn-host

## Scope audited
- `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs` (165 lines)

## Verdict
The compilation host is structurally compact and correctly enforces `allowUnsafe: false`, `deterministic: true`, and in-memory byte emission without leaking loaded assemblies. However, its core security premise—that narrowing metadata references to `System.Private.CoreLib` prevents file system, reflection, and process escape—is invalid under .NET 10 because `System.Private.CoreLib` directly exposes `System.IO.File`, `System.Environment`, `System.Reflection`, and `System.Runtime.InteropServices`. Furthermore, compiler warnings are silently demoted to `Information`, and single-file publish crashes `BuildReferences` due to unhandled empty assembly locations.

## Findings

### [P0] Referencing `System.Private.CoreLib` exposes full file system, environment, reflection, and interop APIs to untrusted strategy code
- **Where:** `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:127`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  AddAssembly(paths, typeof(object));                       // System.Private.CoreLib
  AddAssembly(paths, typeof(Console));                      // System.Console
  AddAssembly(paths, typeof(Enumerable));                   // System.Linq
  AddAssembly(paths, typeof(List<>));                       // System.Collections
  AddAssembly(paths, typeof(Math));                         // System.Runtime.Extensions
  AddAssembly(paths, typeof(YO4X.Mql5.Runtime.IMql5Runtime));
  ```
- **Failure:** The class documentation (lines 16–21) assumes that narrowing metadata references prevents compiled strategies from accessing the file system or executing arbitrary operations. In modern .NET (`net10.0`), `typeof(object).Assembly` is `System.Private.CoreLib.dll`, which contains the monolithic implementation of `System.IO.File`, `System.IO.Directory`, `System.Environment`, `System.Reflection` (`Assembly.Load`, `Type.GetType`), and `System.Runtime.InteropServices.Marshal`. Because references are not restricted to reference-only API contract assemblies and no Roslyn syntax/semantic analyzer validates forbidden member access, any transpiled or injected C# code containing `System.IO.File.ReadAllText(...)` or `System.Environment.FailFast(...)` compiles cleanly and executes in-process with the host application's full OS permissions.
- **Fix:** Compile exclusively against strict reference assemblies (`ref/net10.0`) containing only authorized primitives, or execute a Roslyn `CSharpSyntaxWalker` / diagnostic analyzer prior to emission that rejects invocations of forbidden namespaces and types (`System.IO`, `System.Environment`, `System.Reflection`, `System.Runtime.InteropServices`, `System.Threading`).

### [P1] Compiler warnings indicating broken code generation are demoted to `Information` and ignored
- **Where:** `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:108-110`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  diagnostic.Severity == DiagnosticSeverity.Error
      ? Mql5RestrictedDiagnosticSeverity.Error
      : Mql5RestrictedDiagnosticSeverity.Information,
  ```
- **Failure:** Roslyn diagnostics with `DiagnosticSeverity.Warning` are translated to `Mql5RestrictedDiagnosticSeverity.Information`. Because `options` does not set `generalDiagnosticOption: ReportDiagnostic.Error` or `treatWarningsAsErrors`, `emit.Success` remains `true` when the compiler encounters severe semantic anomalies (such as CS0162 unreachable code, CS0472 value-type comparison with null always constant, or CS8600/CS8602/CS8604 nullability violations under enabled nullable contexts). The miscompiled strategy is returned as `Succeeded: true` and loaded into the trading engine with corrupted logic.
- **Fix:** Preserve `DiagnosticSeverity.Warning` rather than demoting it to `Information`, and configure `CSharpCompilationOptions` with specific diagnostic rules or failure policies for code generation anomalies.

### [P2] `BuildReferences` crashes with `ArgumentNullException` under single-file deployment
- **Where:** `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:137-140`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
  foreach (string facade in (string[])["System.Runtime.dll", "netstandard.dll"])
  {
      string candidate = Path.Combine(directory, facade);
      if (File.Exists(candidate))
  ```
- **Failure:** In single-file published .NET deployments (`PublishSingleFile=true`) or containerized single-file workers without bundle extraction, `Assembly.Location` for bundled core assemblies is `""`. `Path.GetDirectoryName("")` returns `null`. Passing `null` as the first argument to `Path.Combine(directory, facade)` throws an unhandled `ArgumentNullException` during `RoslynMql5CompilationHost` initialization, crashing host startup.
- **Fix:** Check `typeof(object).Assembly.Location` for empty strings and fallback to `AppContext.BaseDirectory` or `System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()`.

### [P2] Unbounded synchronous compilation without `CancellationToken` or timeout enables thread hanging
- **Where:** `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:47-50`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  public Mql5CompilationResult Compile(
      string assemblyName,
      IReadOnlyList<Mql5GeneratedSource> sources)
  {
  ```
- **Failure:** `Compile` is fully synchronous and accepts no `CancellationToken` or timeout parameter, nor does it pass cancellation tokens to `compilation.Emit(image)`. If an untrusted strategy contains deeply nested expressions, massive recursive arrays, or pathological type constructs that cause Roslyn's semantic analysis/emitter to stall, the caller thread is blocked indefinitely and cannot be aborted without terminating the host process.
- **Fix:** Accept a `CancellationToken` (and optional timeout) on `IMql5CompilationHost.Compile` and pass it to `CSharpSyntaxTree.ParseText` and `compilation.Emit`.

### [P3] Diagnostic translation drops source file path in multi-source compilation units
- **Where:** `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:105-114`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
  translated.Add(new Mql5RestrictedDiagnostic(
      diagnostic.Id,
      diagnostic.Severity == DiagnosticSeverity.Error
          ? Mql5RestrictedDiagnosticSeverity.Error
          : Mql5RestrictedDiagnosticSeverity.Information,
      diagnostic.GetMessage(CultureInfo.InvariantCulture),
      span.StartLinePosition.Line + 1,
      span.StartLinePosition.Character + 1));
  ```
- **Failure:** When multiple sources are supplied to `Compile`, `span.Path` contains the originating source identifier from `Mql5GeneratedSource.DisplayPath`. `Translate` discards `span.Path` and only copies `Line` and `Character`, making it impossible for callers to identify which source file in a multi-file compilation produced an error.
- **Fix:** Include `span.Path` in `Mql5RestrictedDiagnostic` or prefix the diagnostic message with the source path.

## Referrals
- `src/Runtime/YO4X.Mql5.Live/LiveStrategyRunner.cs:113` — Loads compiled byte array into in-process `AssemblyLoadContext` without OS-level sandboxing or capability restriction.
- `src/Runtime/YO4X.Mql5.Backtest/Mql5BacktestRunner.cs:136` — Strategy execution occurs in-process without sandbox isolation.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5RestrictedSubsetContracts.cs:3` — `Mql5RestrictedDiagnosticSeverity` enum lacks a `Warning` member, forcing warning demotion.

## Coverage gaps
- `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:137` — Untested when executed in single-file deployment where `typeof(object).Assembly.Location` is empty, concealing the `ArgumentNullException` in `Path.Combine`.
- `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:100` — Untested handling of non-fatal compilation warnings (e.g. CS0162, CS8600) to verify whether `Compile` inappropriately returns `Succeeded: true`.
- `src/Runtime/YO4X.Mql5.Compilation/RoslynMql5CompilationHost.cs:64` — Untested compilation failure across multi-file compilation batches to verify diagnostic path attribution.