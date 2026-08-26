using System.Reflection;
using YO4X.StrategyGovernance;

namespace YO4X.StrategyInputProjection;

internal sealed record ProjectedInput(
    Guid Id,
    Guid StrategyId,
    int Ordinal,
    string Name,
    string? Label,
    string? GroupLabel,
    string DeclaredType,
    string ValueKind,
    string DefaultValue,
    string? EnumTypeName,
    int SourceLine);

internal sealed record ProjectedEnumMember(
    Guid Id,
    Guid StrategyId,
    string EnumTypeName,
    int Ordinal,
    string MemberName,
    long MemberValue,
    string? Label);

internal sealed record ProjectedFile(
    string RelativePath,
    Guid StrategyId,
    IReadOnlyList<ProjectedInput> Inputs,
    IReadOnlyList<ProjectedEnumMember> EnumMembers,
    int SkippedInputCount,
    int SkippedEnumCount);

/// <summary>
/// Turns one lowered MQL5 module into the rows catalog.strategy_inputs and
/// catalog.strategy_enum_members hold.
///
/// Every projected value is read straight out of the IR. Nothing is inferred:
/// an input whose default the front end could not fold, whose name is not a
/// plain identifier, or whose type is not classifiable is skipped and counted
/// rather than given a substitute, and an enumeration with even one member the
/// front end could not fold is skipped whole so a member list is never
/// partially true.
/// </summary>
internal static class Mql5InputProjection
{
    public const int MaximumLabelLength = 400;
    public const int MaximumGroupLabelLength = 200;
    public const int MaximumTypeNameLength = 100;
    public const int MaximumDefaultLength = 4_000;

    /// <summary>
    /// True when the IR carries the trailing-comment label and the
    /// <c>input group</c> heading. These are read by name so this tool builds
    /// against the front end whether or not that work has landed; when it has
    /// not, every projected label is null and the run says so plainly.
    /// </summary>
    public static bool LabelsAvailable =>
        LabelProperty is not null && GroupLabelProperty is not null;

    private static readonly PropertyInfo? LabelProperty =
        FindStringProperty(typeof(Mql5IrInput), "Label");

    private static readonly PropertyInfo? GroupLabelProperty =
        FindStringProperty(typeof(Mql5IrInput), "GroupLabel");

    private static readonly PropertyInfo? EnumMemberLabelProperty =
        FindStringProperty(typeof(Mql5IrEnumMember), "Label");

    public static ProjectedFile Project(
        string corpusSha256,
        string relativePath,
        Mql5IrV2Module module)
    {
        ArgumentNullException.ThrowIfNull(module);

        Guid strategyId = StrategyProjectionIdentity.ForStrategy(corpusSha256, relativePath);
        Dictionary<string, Mql5IrEnumeration> enums = CollectEnums(module);

        var inputs = new List<ProjectedInput>();
        var members = new List<ProjectedEnumMember>();
        // Whether each enumeration type reached this file's member list. An enum input
        // is only emitted when its members did, because a projected enum with no members
        // is a shape the catalog contract refuses: it could not be edited truthfully.
        var enumMembersProjected = new Dictionary<string, bool>(StringComparer.Ordinal);
        var projectedNames = new HashSet<string>(StringComparer.Ordinal);
        int skippedInputs = 0;
        int skippedEnums = 0;
        int ordinal = 0;

        foreach (Mql5IrInput input in module.Inputs)
        {
            if (input is null || input.ArrayRanks.Count > 0)
            {
                skippedInputs++;
                continue;
            }

            string name = input.Name ?? string.Empty;
            string declaredType = input.Type?.Name ?? string.Empty;
            string? canonicalDefault = input.CanonicalDefault;
            if (!IsIdentifier(name)
                || declaredType.Length is 0 or > MaximumTypeNameLength
                || canonicalDefault is null
                || canonicalDefault.Length > MaximumDefaultLength
                || !projectedNames.Add(name))
            {
                skippedInputs++;
                continue;
            }

            string? valueKind = ClassifyValueKind(input.Type!.Scalar);
            if (valueKind is null)
            {
                skippedInputs++;
                continue;
            }

            string? enumTypeName = null;
            if (string.Equals(valueKind, "ENUM", StringComparison.Ordinal))
            {
                enumTypeName = declaredType;
                if (!enumMembersProjected.TryGetValue(enumTypeName, out bool available))
                {
                    // A type declared in the file is projected from its own declaration.
                    // Otherwise it is one of MQL5's built-in enumerations, whose members
                    // are not in the source at all; those come from the measured builtin
                    // catalog rather than being guessed from ordinal position.
                    available = enums.TryGetValue(enumTypeName, out Mql5IrEnumeration? declaration)
                        ? TryProjectEnum(corpusSha256, relativePath, strategyId, declaration, members)
                        : TryProjectBuiltinEnum(
                            corpusSha256,
                            relativePath,
                            strategyId,
                            enumTypeName,
                            members);
                    enumMembersProjected[enumTypeName] = available;
                    if (!available)
                    {
                        skippedEnums++;
                    }
                }

                if (!available)
                {
                    // Emitting this input would produce an ENUM row with no members,
                    // which the catalog contract rejects outright — taking every other
                    // input for the strategy down with it.
                    skippedInputs++;
                    continue;
                }
            }

            inputs.Add(new ProjectedInput(
                StrategyProjectionIdentity.ForInput(corpusSha256, relativePath, ordinal),
                strategyId,
                ordinal,
                name,
                Bound(ReadText(LabelProperty, input), MaximumLabelLength),
                Bound(ReadText(GroupLabelProperty, input), MaximumGroupLabelLength),
                declaredType,
                valueKind,
                canonicalDefault,
                enumTypeName,
                input.Line < 1 ? 1 : input.Line));
            ordinal++;
        }

        return new ProjectedFile(
            relativePath,
            strategyId,
            inputs,
            members,
            skippedInputs,
            skippedEnums);
    }

    /// <summary>
    /// Maps the built-in scalar the front end resolved onto the closed value-kind
    /// set the schema accepts. A type the front end did not resolve to a built-in
    /// scalar is an enumeration, structure or class name; only the first of those
    /// can appear on an input, so it is classified as an enumeration and its
    /// members are projected when the strategy declares them.
    /// </summary>
    private static string? ClassifyValueKind(Mql5IrScalarKind scalar) => scalar switch
    {
        Mql5IrScalarKind.Logical => "LOGICAL",
        Mql5IrScalarKind.Whole8
            or Mql5IrScalarKind.Whole16
            or Mql5IrScalarKind.Whole32
            or Mql5IrScalarKind.Whole64
            or Mql5IrScalarKind.Natural8
            or Mql5IrScalarKind.Natural16
            or Mql5IrScalarKind.Natural32
            or Mql5IrScalarKind.Natural64 => "WHOLE",
        Mql5IrScalarKind.Real32 or Mql5IrScalarKind.Real64 => "REAL",
        Mql5IrScalarKind.Text => "TEXT",
        Mql5IrScalarKind.Moment => "MOMENT",
        Mql5IrScalarKind.Colour => "COLOUR",
        Mql5IrScalarKind.None => "ENUM",
        _ => null
    };

    /// <summary>
    /// Projects one of MQL5's own enumerations, whose members appear nowhere in the
    /// strategy source. The values come from <see cref="Mql5BuiltinConstants"/>, which
    /// measured them from the MetaTrader 5 compiler rather than assuming the members
    /// are ordinal — several of them are not.
    /// </summary>
    private static bool TryProjectBuiltinEnum(
        string corpusSha256,
        string relativePath,
        Guid strategyId,
        string enumTypeName,
        List<ProjectedEnumMember> members)
    {
        IReadOnlyList<Mql5BuiltinConstant> declared = Mql5BuiltinConstants.ByEnum(enumTypeName);
        if (declared.Count == 0)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var projected = new List<ProjectedEnumMember>(declared.Count);
        int ordinal = 0;
        foreach (Mql5BuiltinConstant constant in declared)
        {
            // A member the compiler probe could not fold carries no value, and a
            // dropdown offering it would be showing a number nobody measured.
            if (constant.Value is null
                || !IsIdentifier(constant.Name)
                || !names.Add(constant.Name))
            {
                return false;
            }

            projected.Add(new ProjectedEnumMember(
                StrategyProjectionIdentity.ForEnumMember(
                    corpusSha256,
                    relativePath,
                    enumTypeName,
                    ordinal),
                strategyId,
                enumTypeName,
                ordinal,
                constant.Name,
                constant.Value.Value,
                // The builtin catalog carries no display label; the member name is what
                // MetaTrader itself shows, and inventing prose here would be a guess.
                null));
            ordinal++;
        }

        members.AddRange(projected);
        return true;
    }

    private static bool TryProjectEnum(
        string corpusSha256,
        string relativePath,
        Guid strategyId,
        Mql5IrEnumeration declaration,
        List<ProjectedEnumMember> members)
    {
        var projected = new List<ProjectedEnumMember>(declaration.Members.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        int ordinal = 0;
        foreach (Mql5IrEnumMember member in declaration.Members)
        {
            if (member is null
                || member.FoldedValue is null
                || !IsIdentifier(member.Name ?? string.Empty)
                || !names.Add(member.Name!))
            {
                return false;
            }

            projected.Add(new ProjectedEnumMember(
                StrategyProjectionIdentity.ForEnumMember(
                    corpusSha256,
                    relativePath,
                    declaration.Name,
                    ordinal),
                strategyId,
                declaration.Name,
                ordinal,
                member.Name!,
                member.FoldedValue.Value,
                Bound(ReadText(EnumMemberLabelProperty, member), MaximumLabelLength)));
            ordinal++;
        }

        if (projected.Count == 0)
        {
            return false;
        }

        members.AddRange(projected);
        return true;
    }

    private static Dictionary<string, Mql5IrEnumeration> CollectEnums(Mql5IrV2Module module)
    {
        var enums = new Dictionary<string, Mql5IrEnumeration>(StringComparer.Ordinal);
        foreach (Mql5IrEnumeration declaration in module.Enums)
        {
            Add(enums, declaration);
        }

        foreach (Mql5IrTypeDeclaration type in module.Types)
        {
            CollectNested(enums, type);
        }

        return enums;
    }

    private static void CollectNested(
        Dictionary<string, Mql5IrEnumeration> enums,
        Mql5IrTypeDeclaration type)
    {
        foreach (Mql5IrEnumeration declaration in type.NestedEnums)
        {
            Add(enums, declaration);
        }

        foreach (Mql5IrTypeDeclaration nested in type.NestedTypes)
        {
            CollectNested(enums, nested);
        }
    }

    private static void Add(
        Dictionary<string, Mql5IrEnumeration> enums,
        Mql5IrEnumeration? declaration)
    {
        if (declaration is null
            || declaration.Name.Length is 0 or > MaximumTypeNameLength
            || declaration.Members.Count == 0)
        {
            return;
        }

        enums.TryAdd(declaration.Name, declaration);
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length is 0 or > 64
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string? Bound(string? value, int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length is 0 || trimmed.Length > maximumLength ? null : trimmed;
    }

    private static string? ReadText(PropertyInfo? property, object? instance) =>
        property is null || instance is null ? null : property.GetValue(instance) as string;

    private static PropertyInfo? FindStringProperty(Type declaringType, string propertyName)
    {
        PropertyInfo? property = declaringType.GetProperty(propertyName);
        return property is not null && property.PropertyType == typeof(string)
            ? property
            : null;
    }
}
