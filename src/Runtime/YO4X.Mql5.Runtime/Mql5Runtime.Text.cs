using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 string functions. Every one is <b>Native</b>.
///
/// MQL5 splits its string surface in a way C# does not: the readers take a string and
/// return a value, while the mutators take the subject <c>by reference</c>, edit it in
/// place and return a count or a status flag. <c>StringToUpper</c> returns a
/// <c>bool</c>, not a string, and <c>StringTrimLeft</c> returns how many characters it
/// removed. Those shapes are reproduced here with <c>ref string</c> parameters rather
/// than being tidied into value-returning helpers, because a strategy that writes
/// <c>StringToUpper(name); Print(name);</c> depends on the mutation.
///
/// Casing and comparison are ordinal and invariant throughout. A backtest whose string
/// keys sort differently on a Turkish host is not reproducible, and these strings end
/// up as object names and order comments the strategy parses back.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>StringLen</c>. Returns 0 for an unset string. Native.</summary>
    int StringLen(string? value);

    /// <summary>
    /// MQL5 <c>StringSubstr</c>. A <paramref name="length"/> below zero means "to the
    /// end"; an out-of-range <paramref name="startPosition"/> yields an empty string
    /// rather than an error. Native.
    /// </summary>
    string StringSubstr(string? value, int startPosition, int length = -1);

    /// <summary>
    /// MQL5 <c>StringFind</c>. Ordinal search returning the index of the first match,
    /// or -1 when there is none - never an exception. Native.
    /// </summary>
    int StringFind(string? value, string? match, int startPosition = 0);

    /// <summary>
    /// MQL5 <c>StringReplace</c>. Edits <paramref name="value"/> in place and returns
    /// the number of replacements made, or -1 when <paramref name="find"/> is empty,
    /// which MQL5 treats as an error. Native.
    /// </summary>
    int StringReplace(ref string value, string? find, string? replacement);

    /// <summary>
    /// MQL5 <c>StringSplit</c>. Returns the number of substrings written to
    /// <paramref name="result"/>, or -1 on error. Native.
    /// </summary>
    int StringSplit(string? value, ushort separator, ref string[] result);

    /// <summary>MQL5 <c>StringTrimLeft</c>. Returns the number of characters removed. Native.</summary>
    int StringTrimLeft(ref string value);

    /// <summary>MQL5 <c>StringTrimRight</c>. Returns the number of characters removed. Native.</summary>
    int StringTrimRight(ref string value);

    /// <summary>MQL5 <c>StringToUpper</c>. Edits in place and returns success, not the string. Native.</summary>
    bool StringToUpper(ref string value);

    /// <summary>MQL5 <c>StringToLower</c>. Edits in place and returns success, not the string. Native.</summary>
    bool StringToLower(ref string value);

    /// <summary>MQL5 <c>StringAdd</c>. Appends in place. Native.</summary>
    bool StringAdd(ref string value, string? addition);

    /// <summary>
    /// MQL5 <c>StringConcatenate</c>. Writes the joined arguments into
    /// <paramref name="target"/> and returns the resulting length. Values are rendered
    /// the way <c>Print</c> renders them. Native.
    /// </summary>
    int StringConcatenate(ref string target, params object?[]? arguments);

    /// <summary>MQL5 <c>StringCompare</c>. Returns -1, 0 or 1. Native.</summary>
    int StringCompare(string? first, string? second, bool caseSensitive = true);

    /// <summary>
    /// MQL5 <c>StringGetCharacter</c>. Returns the UTF-16 code unit at
    /// <paramref name="position"/>, or 0 when the index is out of range. Native.
    /// </summary>
    ushort StringGetCharacter(string? value, int position);

    /// <summary>
    /// MQL5 <c>StringSetCharacter</c>. Writing at the end appends; writing a zero
    /// character truncates the string at that position, which is how MQL5 shortens a
    /// string in place. Native.
    /// </summary>
    bool StringSetCharacter(ref string value, int position, ushort character);

    /// <summary>
    /// MQL5 <c>StringInit</c>. Replaces <paramref name="value"/> with
    /// <paramref name="length"/> copies of <paramref name="character"/>; a zero
    /// character empties it. Native.
    /// </summary>
    bool StringInit(ref string value, int length = 0, ushort character = 0);

    /// <summary>MQL5 <c>StringFill</c>. Overwrites every existing character. Native.</summary>
    bool StringFill(ref string value, ushort character);

    /// <summary>
    /// MQL5 <c>StringBufferLen</c>. .NET strings carry no separate capacity, so this
    /// reports the length, which is the only honest answer. Native.
    /// </summary>
    int StringBufferLen(string? value);

    /// <summary>
    /// MQL5 <c>StringReserve</c>. .NET strings are immutable and have no reservable
    /// buffer, so this succeeds without doing anything. Native.
    /// </summary>
    bool StringReserve(ref string value, uint capacity);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public int StringLen(string? value) => value?.Length ?? 0;

    /// <inheritdoc />
    public string StringSubstr(string? value, int startPosition, int length = -1)
    {
        if (string.IsNullOrEmpty(value) || startPosition < 0 || startPosition >= value.Length || length == 0)
        {
            return string.Empty;
        }

        if (length < 0)
        {
            return value[startPosition..];
        }

        int available = value.Length - startPosition;
        return value.Substring(startPosition, Math.Min(length, available));
    }

    /// <inheritdoc />
    public int StringFind(string? value, string? match, int startPosition = 0)
    {
        if (value is null)
        {
            return -1;
        }

        int start = startPosition < 0 ? 0 : startPosition;
        if (start > value.Length)
        {
            return -1;
        }

        if (string.IsNullOrEmpty(match))
        {
            // MQL5 reports an empty needle as found at the search origin.
            return start;
        }

        return value.IndexOf(match, start, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public int StringReplace(ref string value, string? find, string? replacement)
    {
        if (string.IsNullOrEmpty(find))
        {
            SetError(Mql5ErrorCodes.InternalError);
            return -1;
        }

        string subject = value ?? string.Empty;
        string substitute = replacement ?? string.Empty;

        int replacements = 0;
        int cursor = 0;
        StringBuilder builder = new(subject.Length);

        while (true)
        {
            int hit = subject.IndexOf(find, cursor, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }

            builder.Append(subject, cursor, hit - cursor).Append(substitute);
            cursor = hit + find.Length;
            replacements++;
        }

        if (replacements == 0)
        {
            return 0;
        }

        builder.Append(subject, cursor, subject.Length - cursor);
        value = builder.ToString();
        return replacements;
    }

    /// <inheritdoc />
    public int StringSplit(string? value, ushort separator, ref string[] result)
    {
        if (string.IsNullOrEmpty(value))
        {
            result = [];
            return 0;
        }

        result = value.Split((char)separator);
        return result.Length;
    }

    /// <inheritdoc />
    public int StringTrimLeft(ref string value)
    {
        string subject = value ?? string.Empty;
        string trimmed = subject.TrimStart(TrimTargets);
        value = trimmed;
        return subject.Length - trimmed.Length;
    }

    /// <inheritdoc />
    public int StringTrimRight(ref string value)
    {
        string subject = value ?? string.Empty;
        string trimmed = subject.TrimEnd(TrimTargets);
        value = trimmed;
        return subject.Length - trimmed.Length;
    }

    /// <inheritdoc />
    public bool StringToUpper(ref string value)
    {
        value = (value ?? string.Empty).ToUpperInvariant();
        return true;
    }

    /// <inheritdoc />
    public bool StringToLower(ref string value)
    {
        value = (value ?? string.Empty).ToLowerInvariant();
        return true;
    }

    /// <inheritdoc />
    public bool StringAdd(ref string value, string? addition)
    {
        value = (value ?? string.Empty) + (addition ?? string.Empty);
        return true;
    }

    /// <inheritdoc />
    public int StringConcatenate(ref string target, params object?[]? arguments)
    {
        if (arguments is null || arguments.Length == 0)
        {
            target = string.Empty;
            return 0;
        }

        StringBuilder builder = new();
        foreach (object? argument in arguments)
        {
            builder.Append(Mql5Format.Describe(argument));
        }

        target = builder.ToString();
        return target.Length;
    }

    /// <inheritdoc />
    public int StringCompare(string? first, string? second, bool caseSensitive = true)
    {
        int comparison = string.Compare(
            first ?? string.Empty,
            second ?? string.Empty,
            caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        return Math.Sign(comparison);
    }

    /// <inheritdoc />
    public ushort StringGetCharacter(string? value, int position)
    {
        if (value is null || position < 0 || position >= value.Length)
        {
            SetError(Mql5ErrorCodes.StringSmallLength);
            return 0;
        }

        return value[position];
    }

    /// <inheritdoc />
    public bool StringSetCharacter(ref string value, int position, ushort character)
    {
        string subject = value ?? string.Empty;
        if (position < 0 || position > subject.Length)
        {
            SetError(Mql5ErrorCodes.StringSmallLength);
            return false;
        }

        if (position == subject.Length)
        {
            if (character == 0)
            {
                return true;
            }

            value = subject + (char)character;
            return true;
        }

        if (character == 0)
        {
            // MQL5 uses a zero character as a truncation request.
            value = subject[..position];
            return true;
        }

        char[] buffer = subject.ToCharArray();
        buffer[position] = (char)character;
        value = new string(buffer);
        return true;
    }

    /// <inheritdoc />
    public bool StringInit(ref string value, int length = 0, ushort character = 0)
    {
        if (length < 0)
        {
            SetError(Mql5ErrorCodes.StringSmallLength);
            return false;
        }

        value = length == 0 || character == 0 ? string.Empty : new string((char)character, length);
        return true;
    }

    /// <inheritdoc />
    public bool StringFill(ref string value, ushort character)
    {
        string subject = value ?? string.Empty;
        value = subject.Length == 0 ? string.Empty : new string((char)character, subject.Length);
        return true;
    }

    /// <inheritdoc />
    public int StringBufferLen(string? value) => value?.Length ?? 0;

    /// <inheritdoc />
    public bool StringReserve(ref string value, uint capacity)
    {
        value ??= string.Empty;
        return true;
    }

    // The characters MQL5 strips: whitespace plus the NUL that its fixed-width string
    // buffers leave behind.
    private static readonly char[] TrimTargets =
        [' ', '\t', '\n', '\v', '\f', '\r', '\0'];
}
