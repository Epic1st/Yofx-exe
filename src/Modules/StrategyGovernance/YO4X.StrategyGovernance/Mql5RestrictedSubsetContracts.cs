namespace YO4X.StrategyGovernance;

public enum Mql5RestrictedDiagnosticSeverity
{
    Information,
    Error
}

public sealed record Mql5RestrictedDiagnostic(
    string Code,
    Mql5RestrictedDiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column);

public sealed record Mql5RestrictedField(string Name, string Type);

public sealed record Mql5RestrictedStructure(
    string Name,
    IReadOnlyList<Mql5RestrictedField> Fields);

public sealed record Mql5RestrictedEnumMember(string Name, long Value);

public sealed record Mql5RestrictedEnumeration(
    string Name,
    IReadOnlyList<Mql5RestrictedEnumMember> Members);

public sealed record Mql5RestrictedInput(
    string Name,
    string Type,
    string CanonicalValue);

public sealed record Mql5RestrictedIr(
    string SchemaVersion,
    string SourceSha256,
    string IrSha256,
    IReadOnlyList<Mql5RestrictedStructure> Structures,
    IReadOnlyList<Mql5RestrictedEnumeration> Enums,
    IReadOnlyList<Mql5RestrictedInput> Inputs,
    string CanonicalJson);

public sealed record Mql5RestrictedCompilation(
    bool Succeeded,
    Mql5RestrictedIr? Ir,
    IReadOnlyList<Mql5RestrictedDiagnostic> Diagnostics);
