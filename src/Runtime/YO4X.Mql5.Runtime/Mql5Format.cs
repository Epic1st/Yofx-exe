using System.Globalization;
using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// The C-style formatting engine behind <c>StringFormat</c>, <c>PrintFormat</c> and
/// the value rendering that <c>Print</c> and <c>StringConcatenate</c> perform.
///
/// MQL5 inherits printf's grammar verbatim -
/// <c>%[flags][width][.precision][modifier]specifier</c> - so this is a real printf
/// implementation rather than a translation into .NET composite formatting. The two
/// grammars are not interchangeable: <c>%-8.3f</c> has no .NET equivalent, and a
/// naive translation silently drops the flags.
///
/// Every path is culture-invariant. A backtest that formats <c>1.5</c> as
/// <c>"1,5"</c> because the host happened to run under a German locale is not
/// reproducible, and the strings frequently end up in object names and order comments
/// that the strategy later parses back.
///
/// One deliberate divergence from the Microsoft C runtime: exact decimal ties round
/// away from zero rather than to even, because <c>NormalizeDouble</c> is specified to
/// round away from zero and a runtime whose two rounding paths disagree is worse than
/// one that is uniformly off by a half-ulp on the rare exact tie.
/// </summary>
public static class Mql5Format
{
    private const int MaxPrecision = 99;

    /// <summary>
    /// Renders <paramref name="format"/> against <paramref name="arguments"/> using
    /// printf rules. Never throws: a malformed specifier is emitted literally and a
    /// missing argument is treated as zero or an empty string, because
    /// <c>StringFormat</c> is a supported built-in and supported built-ins return a
    /// value rather than raising.
    /// </summary>
    public static string Format(string? format, params object?[]? arguments)
    {
        if (string.IsNullOrEmpty(format))
        {
            return string.Empty;
        }

        object?[] args = arguments ?? [];
        StringBuilder output = new(format.Length + 32);
        int argumentIndex = 0;
        int index = 0;

        while (index < format.Length)
        {
            char current = format[index];
            if (current != '%')
            {
                output.Append(current);
                index++;
                continue;
            }

            int specifierStart = index;
            index++;

            if (index >= format.Length)
            {
                output.Append('%');
                break;
            }

            if (format[index] == '%')
            {
                output.Append('%');
                index++;
                continue;
            }

            bool leftAlign = false;
            bool forceSign = false;
            bool spaceSign = false;
            bool alternate = false;
            bool zeroPad = false;

            while (index < format.Length)
            {
                char flag = format[index];
                if (flag == '-')
                {
                    leftAlign = true;
                }
                else if (flag == '+')
                {
                    forceSign = true;
                }
                else if (flag == ' ')
                {
                    spaceSign = true;
                }
                else if (flag == '#')
                {
                    alternate = true;
                }
                else if (flag == '0')
                {
                    zeroPad = true;
                }
                else
                {
                    break;
                }

                index++;
            }

            int width = -1;
            if (index < format.Length && format[index] == '*')
            {
                index++;
                long supplied = ToInt64(Next(args, ref argumentIndex));
                if (supplied < 0)
                {
                    leftAlign = true;
                    supplied = -supplied;
                }

                width = supplied > 4096 ? 4096 : (int)supplied;
            }
            else
            {
                int parsed = 0;
                bool any = false;
                while (index < format.Length && char.IsAsciiDigit(format[index]))
                {
                    parsed = parsed > 4096 ? 4096 : (parsed * 10) + (format[index] - '0');
                    index++;
                    any = true;
                }

                if (any)
                {
                    width = parsed;
                }
            }

            int precision = -1;
            if (index < format.Length && format[index] == '.')
            {
                index++;
                if (index < format.Length && format[index] == '*')
                {
                    index++;
                    long supplied = ToInt64(Next(args, ref argumentIndex));
                    precision = supplied < 0 ? -1 : (supplied > MaxPrecision ? MaxPrecision : (int)supplied);
                }
                else
                {
                    int parsed = 0;
                    while (index < format.Length && char.IsAsciiDigit(format[index]))
                    {
                        parsed = parsed > MaxPrecision ? MaxPrecision : (parsed * 10) + (format[index] - '0');
                        index++;
                    }

                    precision = parsed > MaxPrecision ? MaxPrecision : parsed;
                }
            }

            index = SkipLengthModifier(format, index);

            if (index >= format.Length)
            {
                output.Append(format, specifierStart, format.Length - specifierStart);
                break;
            }

            char conversion = format[index];
            index++;

            string rendered = Render(
                conversion,
                args,
                ref argumentIndex,
                leftAlign,
                forceSign,
                spaceSign,
                alternate,
                zeroPad,
                width,
                precision,
                out bool handled);

            if (handled)
            {
                output.Append(rendered);
            }
            else
            {
                output.Append(format, specifierStart, index - specifierStart);
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Renders one value the way <c>Print</c>, <c>Comment</c>, <c>Alert</c> and
    /// <c>StringConcatenate</c> do when no format string is involved.
    ///
    /// MQL5 documents doubles here as printed "with up to 16 significant digits, in
    /// whichever of the traditional and scientific forms is more compact", which is
    /// printf's <c>%.16g</c>; floats get five decimals; and <c>bool</c> prints as
    /// <c>true</c> or <c>false</c> rather than as 1 or 0.
    /// </summary>
    public static string Describe(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => General(number, 16, upper: false, keepTrailingZeros: false),
        float number => Fixed(number, 5),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        char character => character.ToString(),
        DateTime moment => moment.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// printf's <c>%f</c>: <paramref name="value"/> with exactly
    /// <paramref name="digits"/> digits after the decimal point.
    /// </summary>
    public static string Fixed(double value, int digits)
    {
        if (TryFormatSpecial(value, upper: false, out string? special))
        {
            return special;
        }

        int clamped = digits < 0 ? 0 : (digits > MaxPrecision ? MaxPrecision : digits);
        return value.ToString("F" + clamped.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// printf's <c>%e</c>: <paramref name="value"/> in scientific notation with
    /// <paramref name="precision"/> digits after the decimal point and an exponent of
    /// at least two digits, which is what the C runtime emits and what .NET's own
    /// <c>"E"</c> specifier does not - it pads the exponent to three.
    /// </summary>
    public static string Scientific(double value, int precision, bool upper)
    {
        if (TryFormatSpecial(value, upper, out string? special))
        {
            return special;
        }

        int clamped = precision < 0 ? 6 : (precision > MaxPrecision ? MaxPrecision : precision);
        string formatted = value.ToString("E" + clamped.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return NormaliseExponent(formatted, upper);
    }

    /// <summary>
    /// printf's <c>%g</c>: whichever of the fixed and scientific forms is shorter for
    /// <paramref name="precision"/> significant digits, with trailing fractional
    /// zeros removed unless <paramref name="keepTrailingZeros"/> is set - printf's
    /// <c>#</c> flag.
    /// </summary>
    public static string General(double value, int precision, bool upper, bool keepTrailingZeros)
    {
        if (TryFormatSpecial(value, upper, out string? special))
        {
            return special;
        }

        int significant = precision < 0 ? 6 : (precision == 0 ? 1 : (precision > MaxPrecision ? MaxPrecision : precision));
        int exponent = DecimalExponent(value, significant);

        string body = exponent >= -4 && exponent < significant
            ? Fixed(value, Math.Max(0, significant - 1 - exponent))
            : Scientific(value, significant - 1, upper);

        return keepTrailingZeros ? body : StripTrailingZeros(body);
    }

    /// <summary>Coerces a boxed MQL5 value to the signed integer printf would read.</summary>
    public static long ToInt64(object? value) => value switch
    {
        null => 0,
        bool flag => flag ? 1 : 0,
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => unchecked((long)number),
        char character => character,
        float number => ToInt64FromDouble(number),
        double number => ToInt64FromDouble(number),
        decimal number => number >= long.MaxValue ? long.MaxValue : (number <= long.MinValue ? long.MinValue : (long)number),
        DateTime moment => Mql5Time.FromDateTime(moment),
        string text => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0,
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture),
        _ => 0
    };

    /// <summary>Coerces a boxed MQL5 value to the unsigned integer printf would read.</summary>
    public static ulong ToUInt64(object? value) => value switch
    {
        ulong number => number,
        uint number => number,
        byte number => number,
        ushort number => number,
        _ => unchecked((ulong)ToInt64(value))
    };

    /// <summary>Coerces a boxed MQL5 value to the double printf would read.</summary>
    public static double ToDouble(object? value) => value switch
    {
        null => 0,
        bool flag => flag ? 1 : 0,
        double number => number,
        float number => number,
        decimal number => (double)number,
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => number,
        char character => character,
        DateTime moment => Mql5Time.FromDateTime(moment),
        string text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0,
        Enum enumeration => Convert.ToDouble(enumeration, CultureInfo.InvariantCulture),
        _ => 0
    };

    private static long ToInt64FromDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value >= 9.2233720368547758E18)
        {
            return long.MaxValue;
        }

        if (value <= -9.2233720368547758E18)
        {
            return long.MinValue;
        }

        return (long)value;
    }

    private static object? Next(object?[] arguments, ref int index)
    {
        if (index >= arguments.Length)
        {
            return null;
        }

        return arguments[index++];
    }

    private static int SkipLengthModifier(string format, int index)
    {
        while (index < format.Length)
        {
            char modifier = format[index];
            if (modifier is 'h' or 'l' or 'L' or 'j' or 'z' or 't' or 'q' or 'w')
            {
                index++;
                continue;
            }

            if (modifier == 'I')
            {
                index++;
                if (index + 1 < format.Length && format[index] == '3' && format[index + 1] == '2')
                {
                    index += 2;
                }
                else if (index + 1 < format.Length && format[index] == '6' && format[index + 1] == '4')
                {
                    index += 2;
                }

                continue;
            }

            break;
        }

        return index;
    }

    private static string Render(
        char conversion,
        object?[] arguments,
        ref int argumentIndex,
        bool leftAlign,
        bool forceSign,
        bool spaceSign,
        bool alternate,
        bool zeroPad,
        int width,
        int precision,
        out bool handled)
    {
        handled = true;

        switch (conversion)
        {
            case 'd':
            case 'i':
            {
                long value = ToInt64(Next(arguments, ref argumentIndex));
                string digits = value == long.MinValue
                    ? "9223372036854775808"
                    : Math.Abs(value).ToString(CultureInfo.InvariantCulture);
                digits = ApplyIntegerPrecision(digits, precision);
                string sign = value < 0 ? "-" : (forceSign ? "+" : (spaceSign ? " " : string.Empty));
                return Pad(sign, string.Empty, digits, width, leftAlign, zeroPad && precision < 0);
            }

            case 'u':
            {
                ulong value = ToUInt64(Next(arguments, ref argumentIndex));
                string digits = ApplyIntegerPrecision(value.ToString(CultureInfo.InvariantCulture), precision);
                return Pad(string.Empty, string.Empty, digits, width, leftAlign, zeroPad && precision < 0);
            }

            case 'o':
            {
                ulong value = ToUInt64(Next(arguments, ref argumentIndex));
                string digits = ApplyIntegerPrecision(Convert.ToString((long)value, 8), precision);
                string prefix = alternate && !digits.StartsWith('0') ? "0" : string.Empty;
                return Pad(string.Empty, prefix, digits, width, leftAlign, zeroPad && precision < 0);
            }

            case 'x':
            case 'X':
            {
                ulong value = ToUInt64(Next(arguments, ref argumentIndex));
                string digits = value.ToString(conversion == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                digits = ApplyIntegerPrecision(digits, precision);
                string prefix = alternate && value != 0 ? (conversion == 'x' ? "0x" : "0X") : string.Empty;
                return Pad(string.Empty, prefix, digits, width, leftAlign, zeroPad && precision < 0);
            }

            case 'p':
            {
                ulong value = ToUInt64(Next(arguments, ref argumentIndex));
                return Pad(string.Empty, string.Empty, value.ToString("X8", CultureInfo.InvariantCulture), width, leftAlign, zeroPad);
            }

            case 'f':
            case 'F':
            {
                double value = ToDouble(Next(arguments, ref argumentIndex));
                return PadSignedNumber(Fixed(Math.Abs(value), precision < 0 ? 6 : precision), value, forceSign, spaceSign, width, leftAlign, zeroPad);
            }

            case 'e':
            case 'E':
            {
                double value = ToDouble(Next(arguments, ref argumentIndex));
                string body = Scientific(Math.Abs(value), precision < 0 ? 6 : precision, conversion == 'E');
                return PadSignedNumber(body, value, forceSign, spaceSign, width, leftAlign, zeroPad);
            }

            case 'g':
            case 'G':
            {
                double value = ToDouble(Next(arguments, ref argumentIndex));
                string body = General(Math.Abs(value), precision, conversion == 'G', alternate);
                return PadSignedNumber(body, value, forceSign, spaceSign, width, leftAlign, zeroPad);
            }

            case 'a':
            case 'A':
            {
                double value = ToDouble(Next(arguments, ref argumentIndex));
                string body = General(Math.Abs(value), precision, conversion == 'A', alternate);
                return PadSignedNumber(body, value, forceSign, spaceSign, width, leftAlign, zeroPad);
            }

            case 'c':
            {
                long value = ToInt64(Next(arguments, ref argumentIndex));
                string body = value is > 0 and <= 0xFFFF ? ((char)value).ToString() : string.Empty;
                return Pad(string.Empty, string.Empty, body, width, leftAlign, zeroPad: false);
            }

            case 's':
            {
                string body = Describe(Next(arguments, ref argumentIndex));
                if (precision >= 0 && body.Length > precision)
                {
                    body = body[..precision];
                }

                return Pad(string.Empty, string.Empty, body, width, leftAlign, zeroPad: false);
            }

            case 'n':
            {
                // printf's %n writes back through a pointer. There are no pointers
                // here, so the argument is consumed and nothing is emitted.
                Next(arguments, ref argumentIndex);
                return string.Empty;
            }

            default:
                handled = false;
                return string.Empty;
        }
    }

    private static string ApplyIntegerPrecision(string digits, int precision)
    {
        if (precision < 0)
        {
            return digits;
        }

        if (precision == 0 && digits == "0")
        {
            return string.Empty;
        }

        return digits.Length >= precision ? digits : digits.PadLeft(precision, '0');
    }

    private static string PadSignedNumber(string body, double value, bool forceSign, bool spaceSign, int width, bool leftAlign, bool zeroPad)
    {
        bool negative = value < 0 || (value == 0 && double.IsNegative(value));
        string sign = negative ? "-" : (forceSign ? "+" : (spaceSign ? " " : string.Empty));
        bool numeric = !double.IsNaN(value) && !double.IsInfinity(value);
        return Pad(sign, string.Empty, body, width, leftAlign, zeroPad && numeric);
    }

    private static string Pad(string sign, string prefix, string body, int width, bool leftAlign, bool zeroPad)
    {
        int total = sign.Length + prefix.Length + body.Length;
        if (width <= total)
        {
            return sign + prefix + body;
        }

        int fill = width - total;
        if (leftAlign)
        {
            return sign + prefix + body + new string(' ', fill);
        }

        return zeroPad
            ? sign + prefix + new string('0', fill) + body
            : new string(' ', fill) + sign + prefix + body;
    }

    private static bool TryFormatSpecial(double value, bool upper, out string result)
    {
        if (double.IsNaN(value))
        {
            result = upper ? "NAN" : "nan";
            return true;
        }

        if (double.IsPositiveInfinity(value))
        {
            result = upper ? "INF" : "inf";
            return true;
        }

        if (double.IsNegativeInfinity(value))
        {
            result = upper ? "-INF" : "-inf";
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static int DecimalExponent(double value, int significantDigits)
    {
        if (value == 0)
        {
            return 0;
        }

        string probe = Math.Abs(value).ToString(
            "E" + Math.Max(0, significantDigits - 1).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

        int marker = probe.IndexOf('E', StringComparison.Ordinal);
        return marker < 0
            ? 0
            : int.Parse(probe[(marker + 1)..], NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static string NormaliseExponent(string formatted, bool upper)
    {
        int marker = formatted.IndexOf('E', StringComparison.Ordinal);
        if (marker < 0)
        {
            return formatted;
        }

        string mantissa = formatted[..marker];
        char sign = formatted[marker + 1];
        string exponentDigits = formatted[(marker + 2)..].TrimStart('0');
        if (exponentDigits.Length < 2)
        {
            exponentDigits = exponentDigits.PadLeft(2, '0');
        }

        return string.Concat(mantissa, upper ? "E" : "e", sign.ToString(), exponentDigits);
    }

    private static string StripTrailingZeros(string body)
    {
        int marker = body.IndexOfAny(['e', 'E']);
        string mantissa = marker < 0 ? body : body[..marker];
        string tail = marker < 0 ? string.Empty : body[marker..];

        if (mantissa.Contains('.', StringComparison.Ordinal))
        {
            mantissa = mantissa.TrimEnd('0');
            if (mantissa.EndsWith('.'))
            {
                mantissa = mantissa[..^1];
            }
        }

        return mantissa + tail;
    }
}
