using System.Globalization;
using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 conversion functions. Every one is <b>Native</b>, and between them they carry
/// more callsites than any other group in the corpus after the chart family.
///
/// <c>NormalizeDouble</c> is the one to get right. Every price a strategy computes
/// passes through it before being sent, so its rounding mode is not a detail: MQL5
/// rounds half away from zero and clamps <c>digits</c> to 0 to 8. Rounding half to
/// even instead would move a stop by one point on every exact tie, on every order, in
/// every strategy.
///
/// Everything here is culture-invariant. A strategy that formats a price as
/// <c>"1,2345"</c> because the host runs under a comma-decimal locale writes that
/// string into an order comment or an object name and then fails to parse it back.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>
    /// MQL5 <c>NormalizeDouble</c>. Rounds <paramref name="value"/> to
    /// <paramref name="digits"/> decimal places, half away from zero, with
    /// <paramref name="digits"/> clamped to MQL5's documented 0 to 8 range. NaN and the
    /// infinities pass through unchanged. Native.
    /// </summary>
    double NormalizeDouble(double value, int digits);

    /// <summary>
    /// MQL5 <c>DoubleToString</c>. A non-negative <paramref name="digits"/> up to 16
    /// gives fixed notation with that many decimals; a negative value from -1 to -16
    /// gives scientific notation with that many decimals, which is what MQL5 documents.
    /// Native.
    /// </summary>
    string DoubleToString(double value, int digits = 8);

    /// <summary>
    /// MQL5 <c>IntegerToString</c>. Right-aligns to <paramref name="length"/> using
    /// <paramref name="fillSymbol"/>, which defaults to a space. Native.
    /// </summary>
    string IntegerToString(long number, int length = 0, ushort fillSymbol = ' ');

    /// <summary>
    /// MQL5 <c>StringToDouble</c>. Reads the leading numeric prefix, as C's
    /// <c>atof</c> does, and yields 0 when there is none. Native.
    /// </summary>
    double StringToDouble(string? value);

    /// <summary>
    /// MQL5 <c>StringToInteger</c>. Reads the leading integer prefix, as C's
    /// <c>atol</c> does, and yields 0 when there is none. Native.
    /// </summary>
    long StringToInteger(string? value);

    /// <summary>
    /// MQL5 <c>TimeToString</c>. <paramref name="mode"/> is a bitmask of
    /// <see cref="Mql5Constants.TimeDate"/>, <see cref="Mql5Constants.TimeMinutes"/> and
    /// <see cref="Mql5Constants.TimeSeconds"/>. Native.
    /// </summary>
    string TimeToString(long value, int mode = Mql5Constants.TimeDate | Mql5Constants.TimeMinutes);

    /// <summary>
    /// MQL5 <c>StringToTime</c>. Accepts the <c>yyyy.mm.dd hh:mi:ss</c> family MQL5
    /// documents, with <c>/</c> and <c>-</c> as alternative date separators and either
    /// half optional. Returns 0 when nothing parses. Native.
    /// </summary>
    long StringToTime(string? value);

    /// <summary>
    /// MQL5 <c>StringFormat</c>. Full printf grammar - flags, width, precision and
    /// length modifiers - rendered invariantly. Native.
    /// </summary>
    string StringFormat(string? format, params object?[]? arguments);

    /// <summary>MQL5 <c>CharToString</c>. Native.</summary>
    string CharToString(byte characterCode);

    /// <summary>MQL5 <c>ShortToString</c>: one UTF-16 code unit as a string. Native.</summary>
    string ShortToString(ushort symbolCode);

    /// <summary>
    /// MQL5 <c>ColorToString</c>. Yields the <c>clrXxx</c> name when
    /// <paramref name="useColorName"/> is set and the colour has one, and MQL5's
    /// <c>"R,G,B"</c> triple otherwise. Native.
    /// </summary>
    string ColorToString(int colorValue, bool useColorName = false);

    /// <summary>MQL5 <c>StringToColor</c>. Returns <see cref="Mql5Constants.ColorNone"/> on failure. Native.</summary>
    int StringToColor(string? value);

    /// <summary>
    /// MQL5 <c>ColorToARGB</c>. Converts MQL5's <c>0x00BBGGRR</c> colour into the
    /// <c>0xAARRGGBB</c> form the drawing API wants. Native.
    /// </summary>
    uint ColorToArgb(int color, byte alpha = 255);

    /// <summary>
    /// MQL5 <c>EnumToString</c>. MQL5 enumeration members do not survive lowering as
    /// named entities, so this returns the CLR enumeration name when it has one and the
    /// invariant text of the value otherwise. Native.
    /// </summary>
    string EnumToString(object? value);

    /// <summary>MQL5 <c>CharArrayToString</c>. Native.</summary>
    string CharArrayToString(byte[]? array, int start = 0, int count = -1, uint codepage = 0);

    /// <summary>MQL5 <c>ShortArrayToString</c>. Native.</summary>
    string ShortArrayToString(ushort[]? array, int start = 0, int count = -1);

    /// <summary>
    /// MQL5 <c>StringToCharArray</c>. Returns the number of elements written, including
    /// the terminating zero MQL5 appends when no explicit count was given. Native.
    /// </summary>
    int StringToCharArray(string? text, ref byte[]? array, int start = 0, int count = -1, uint codepage = 0);

    /// <summary>MQL5 <c>CharArrayToString</c> over a signed <c>char</c> buffer. Native.</summary>
    string CharArrayToString(sbyte[]? array, int start = 0, int count = -1, uint codepage = 0);

    /// <summary>MQL5 <c>StringToCharArray</c> into a signed <c>char</c> buffer. Native.</summary>
    int StringToCharArray(string? text, ref sbyte[]? array, int start = 0, int count = -1, uint codepage = 0);

    /// <summary>MQL5 <c>StringToShortArray</c>. Returns the number of elements written. Native.</summary>
    int StringToShortArray(string? text, ref ushort[]? array, int start = 0, int count = -1);

    /// <summary>
    /// MQL5 <c>CharArrayToStruct</c>. Reinterpreting a byte buffer as an MQL5 structure
    /// has no counterpart once the structure has been lowered to a CLR type, so this
    /// reports failure and sets <c>ERR_STRUCT_WITHOBJECTS_ORCLASS</c> rather than
    /// fabricating a value. Native.
    /// </summary>
    bool CharArrayToStruct(byte[]? array, uint startPosition = 0);

    /// <summary>
    /// MQL5 <c>StructToCharArray</c>. See <see cref="CharArrayToStruct"/>: reports
    /// failure rather than fabricating a serialisation. Native.
    /// </summary>
    bool StructToCharArray(ref byte[]? array, uint startPosition = 0);
}

public sealed partial class Mql5Runtime
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm", "yyyy.MM.dd",
        "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/MM/dd",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "yyyy.M.d H:m:s", "yyyy.M.d H:m", "yyyy.M.d",
        "HH:mm:ss", "HH:mm"
    ];

    /// <inheritdoc />
    public double NormalizeDouble(double value, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value;
        }

        // MQL5 documents digits as 0 to 8 and clamps outside that range rather than
        // failing, which matters because strategies pass Digits() straight in and a
        // five-digit symbol on a broker that reports 10 would otherwise throw.
        int clamped = digits < 0 ? 0 : (digits > 8 ? 8 : digits);
        return Math.Round(value, clamped, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public string DoubleToString(double value, int digits = 8)
    {
        if (digits >= 0)
        {
            return Mql5Format.Fixed(value, Math.Min(digits, 16));
        }

        int precision = Math.Min(-digits, 16);
        return Mql5Format.Scientific(value, precision, upper: false);
    }

    /// <inheritdoc />
    public string IntegerToString(long number, int length = 0, ushort fillSymbol = ' ')
    {
        string digits = number.ToString(CultureInfo.InvariantCulture);
        if (length <= digits.Length)
        {
            return digits;
        }

        char fill = fillSymbol == 0 ? ' ' : (char)fillSymbol;
        return digits.PadLeft(length, fill);
    }

    /// <inheritdoc />
    public double StringToDouble(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        ReadOnlySpan<char> span = value.AsSpan().TrimStart();
        int length = 0;
        bool seenDigit = false;
        bool seenDot = false;
        bool seenExponent = false;

        while (length < span.Length)
        {
            char current = span[length];

            if (char.IsAsciiDigit(current))
            {
                seenDigit = true;
            }
            else if ((current == '-' || current == '+') && (length == 0 || (seenExponent && (span[length - 1] is 'e' or 'E'))))
            {
                // A sign is only legal at the very front or straight after the exponent.
            }
            else if (current == '.' && !seenDot && !seenExponent)
            {
                seenDot = true;
            }
            else if ((current is 'e' or 'E') && seenDigit && !seenExponent)
            {
                seenExponent = true;
            }
            else
            {
                break;
            }

            length++;
        }

        if (!seenDigit)
        {
            return 0;
        }

        ReadOnlySpan<char> candidate = span[..length];
        while (candidate.Length > 0 && !char.IsAsciiDigit(candidate[^1]))
        {
            candidate = candidate[..^1];
        }

        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
    }

    /// <inheritdoc />
    public long StringToInteger(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        ReadOnlySpan<char> span = value.AsSpan().TrimStart();
        int length = 0;
        if (length < span.Length && (span[length] == '-' || span[length] == '+'))
        {
            length++;
        }

        int digitStart = length;
        while (length < span.Length && char.IsAsciiDigit(span[length]))
        {
            length++;
        }

        if (length == digitStart)
        {
            return 0;
        }

        return long.TryParse(span[..length], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : (span[0] == '-' ? long.MinValue : long.MaxValue);
    }

    /// <inheritdoc />
    public string TimeToString(long value, int mode = Mql5Constants.TimeDate | Mql5Constants.TimeMinutes)
    {
        int effective = (mode & (Mql5Constants.TimeDate | Mql5Constants.TimeMinutes | Mql5Constants.TimeSeconds)) == 0
            ? Mql5Constants.TimeDate | Mql5Constants.TimeMinutes
            : mode;

        DateTime moment = Mql5Time.ToDateTime(value);
        bool wantsDate = (effective & Mql5Constants.TimeDate) != 0;
        bool wantsSeconds = (effective & Mql5Constants.TimeSeconds) != 0;
        bool wantsMinutes = (effective & Mql5Constants.TimeMinutes) != 0;

        string date = wantsDate ? moment.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) : string.Empty;
        string time = wantsSeconds
            ? moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : (wantsMinutes ? moment.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty);

        if (date.Length == 0)
        {
            return time;
        }

        return time.Length == 0 ? date : string.Concat(date, " ", time);
    }

    /// <inheritdoc />
    public long StringToTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        string trimmed = value.Trim();
        if (DateTime.TryParseExact(
                trimmed,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime exact))
        {
            return Mql5Time.FromDateTime(exact);
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime loose))
        {
            return Mql5Time.FromDateTime(loose);
        }

        SetError(Mql5ErrorCodes.InvalidDatetime);
        return 0;
    }

    /// <inheritdoc />
    public string StringFormat(string? format, params object?[]? arguments) => Mql5Format.Format(format, arguments);

    /// <inheritdoc />
    public string CharToString(byte characterCode) => ((char)characterCode).ToString();

    /// <inheritdoc />
    public string ShortToString(ushort symbolCode) => ((char)symbolCode).ToString();

    /// <inheritdoc />
    public string ColorToString(int colorValue, bool useColorName = false)
    {
        if (useColorName)
        {
            string? name = Mql5Colors.Name(colorValue);
            if (name is not null)
            {
                return "clr" + name;
            }
        }

        (int red, int green, int blue) = Mql5Colors.Unpack(colorValue);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{red},{green},{blue}");
    }

    /// <inheritdoc />
    public int StringToColor(string? value)
    {
        if (Mql5Colors.TryParse(value, out int color))
        {
            return color;
        }

        SetError(Mql5ErrorCodes.InvalidParameter);
        return Mql5Constants.ColorNone;
    }

    /// <inheritdoc />
    public uint ColorToArgb(int color, byte alpha = 255)
    {
        (int red, int green, int blue) = Mql5Colors.Unpack(color);
        return ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
    }

    /// <inheritdoc />
    public string EnumToString(object? value) => value switch
    {
        null => string.Empty,
        Enum enumeration => enumeration.ToString(),
        _ => Mql5Format.Describe(value)
    };

    /// <inheritdoc />
    public string CharArrayToString(byte[]? array, int start = 0, int count = -1, uint codepage = 0)
    {
        if (array is null || array.Length == 0)
        {
            return string.Empty;
        }

        int from = Math.Clamp(start, 0, array.Length);
        int available = array.Length - from;
        int span = count < 0 ? available : Math.Min(count, available);
        if (span <= 0)
        {
            return string.Empty;
        }

        // MQL5 stops at the first NUL, because its char arrays are C strings.
        int terminator = Array.IndexOf(array, (byte)0, from, span);
        if (terminator >= 0)
        {
            span = terminator - from;
        }

        return span <= 0 ? string.Empty : Codepage(codepage).GetString(array, from, span);
    }

    /// <inheritdoc />
    public string ShortArrayToString(ushort[]? array, int start = 0, int count = -1)
    {
        if (array is null || array.Length == 0)
        {
            return string.Empty;
        }

        int from = Math.Clamp(start, 0, array.Length);
        int available = array.Length - from;
        int span = count < 0 ? available : Math.Min(count, available);

        StringBuilder builder = new(Math.Max(0, span));
        for (int index = from; index < from + span; index++)
        {
            if (array[index] == 0)
            {
                break;
            }

            builder.Append((char)array[index]);
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public int StringToCharArray(string? text, ref byte[]? array, int start = 0, int count = -1, uint codepage = 0)
    {
        string subject = text ?? string.Empty;
        int from = Math.Max(0, start);

        byte[] encoded = Codepage(codepage).GetBytes(subject);
        bool appendTerminator = count < 0;
        int span = count < 0 ? encoded.Length : Math.Min(count, encoded.Length);
        int written = span + (appendTerminator ? 1 : 0);

        array ??= [];
        if (array.Length < from + written)
        {
            byte[] grown = array;
            Array.Resize(ref grown, from + written);
            array = grown;
        }

        Array.Copy(encoded, 0, array, from, span);
        if (appendTerminator)
        {
            array[from + span] = 0;
        }

        return written;
    }

    /// <inheritdoc />
    /// <remarks>
    /// MQL5 declares its byte buffers inconsistently — <c>StringToCharArray</c> writes into a
    /// <c>uchar</c> array while <c>WebRequest</c> reads a <c>char</c> one — and then lets a program
    /// pass either to either. The compiler was asked directly: a file passing both a <c>char[]</c>
    /// and a <c>uchar[]</c> to all three of these functions compiles with no error or warning. So
    /// both spellings are accepted here rather than one being chosen, because choosing would refuse
    /// programs MQL5 accepts.
    ///
    /// The two differ only in how the top bit is read, so the conversion is a reinterpretation and
    /// never loses information.
    /// </remarks>
    public string CharArrayToString(sbyte[]? array, int start = 0, int count = -1, uint codepage = 0)
        => CharArrayToString(ToUnsigned(array), start, count, codepage);

    /// <inheritdoc />
    /// <remarks>See <see cref="CharArrayToString(sbyte[], int, int, uint)"/> for why both signed
    /// and unsigned buffers are accepted. The array is written back because the callee grows it.</remarks>
    public int StringToCharArray(string? text, ref sbyte[]? array, int start = 0, int count = -1, uint codepage = 0)
    {
        byte[]? unsigned = ToUnsigned(array);
        int written = StringToCharArray(text, ref unsigned, start, count, codepage);
        array = ToSigned(unsigned);
        return written;
    }

    private static byte[]? ToUnsigned(sbyte[]? array)
    {
        if (array is null)
        {
            return null;
        }

        var converted = new byte[array.Length];
        for (int index = 0; index < array.Length; index++)
        {
            converted[index] = unchecked((byte)array[index]);
        }

        return converted;
    }

    private static sbyte[]? ToSigned(byte[]? array)
    {
        if (array is null)
        {
            return null;
        }

        var converted = new sbyte[array.Length];
        for (int index = 0; index < array.Length; index++)
        {
            converted[index] = unchecked((sbyte)array[index]);
        }

        return converted;
    }

    /// <inheritdoc />
    public int StringToShortArray(string? text, ref ushort[]? array, int start = 0, int count = -1)
    {
        string subject = text ?? string.Empty;
        int from = Math.Max(0, start);
        int span = count < 0 ? subject.Length : Math.Min(count, subject.Length);
        bool appendTerminator = count < 0;
        int written = span + (appendTerminator ? 1 : 0);

        array ??= [];
        if (array.Length < from + written)
        {
            ushort[] grown = array;
            Array.Resize(ref grown, from + written);
            array = grown;
        }

        for (int index = 0; index < span; index++)
        {
            array[from + index] = subject[index];
        }

        if (appendTerminator)
        {
            array[from + span] = 0;
        }

        return written;
    }

    /// <inheritdoc />
    public bool CharArrayToStruct(byte[]? array, uint startPosition = 0)
    {
        SetError(Mql5ErrorCodes.StructWithObjectsOrClass);
        return false;
    }

    /// <inheritdoc />
    public bool StructToCharArray(ref byte[]? array, uint startPosition = 0)
    {
        SetError(Mql5ErrorCodes.StructWithObjectsOrClass);
        return false;
    }

    // MQL5 codepage 0 means "the terminal's ANSI codepage". There is no terminal here,
    // so Latin-1 stands in: it is the only single-byte encoding that round-trips every
    // byte value, which keeps CharArrayToString and StringToCharArray inverses of one
    // another whatever the host locale is.
    private static Encoding Codepage(uint codepage) => codepage switch
    {
        65001 => Encoding.UTF8,
        1200 => Encoding.Unicode,
        1201 => Encoding.BigEndianUnicode,
        _ => Encoding.Latin1
    };
}
