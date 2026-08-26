namespace YO4X.Mql5.CodeGen;

/// <summary>
/// The fixed helper text emitted into every generated compilation unit.
///
/// These helpers exist so that the MQL5 semantics that differ from C# live in one
/// readable place instead of being smeared across the emitter as ad-hoc casts:
/// truth of a non-boolean, arithmetic on <c>bool</c>, <c>datetime</c> as seconds
/// since the epoch, and <c>+</c> meaning concatenation once either side is text.
///
/// The text is a compile-time constant, which is what makes the generator
/// byte-deterministic: identical module in, identical file out.
///
/// It is emitted as a <c>file</c>-scoped type so that any number of generated
/// strategies can be compiled together without colliding.
/// </summary>
internal static class Mql5EmittedHelpers
{
    /// <summary>The verbatim helper source.</summary>
    public const string Source = """
/// <summary>MQL5 semantics that C# does not share, emitted verbatim by the generator.</summary>
file static class Mql5Ops
{
    /// <summary>The MQL5 epoch: <c>datetime</c> is seconds since 1970-01-01 UTC.</summary>
    private static readonly System.DateTime Epoch =
        new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

    /// <summary>MQL5 conditions accept any scalar; C# accepts only <c>bool</c>.</summary>
    public static bool Truth(bool value) => value;

    /// <summary>Non-zero is true.</summary>
    public static bool Truth(long value) => value != 0L;

    /// <summary>Non-zero is true.</summary>
    public static bool Truth(ulong value) => value != 0UL;

    /// <summary>Non-zero is true. NaN is not zero, so NaN is true, as in MQL5.</summary>
    public static bool Truth(double value) => value != 0D;

    /// <summary>A datetime is true when it is not the epoch, i.e. when its seconds are non-zero.</summary>
    public static bool Truth(System.DateTime value) => value != Epoch;

    /// <summary>A string is true when it is neither null nor empty.</summary>
    public static bool Truth(string? value) => !string.IsNullOrEmpty(value);

    /// <summary>A pointer is true when it is not null.</summary>
    public static bool Truth(object? value) => value is not null;

    /// <summary>MQL5 promotes <c>bool</c> to 0 or 1 in arithmetic; C# refuses to.</summary>
    public static int Num(bool value) => value ? 1 : 0;

    /// <summary>Seconds since the MQL5 epoch.</summary>
    public static long Seconds(System.DateTime value) =>
        (long)(value - Epoch).TotalSeconds;

    /// <summary>
    /// MQL5 <c>datetime</c> is an integer count of seconds, and the runtime returns
    /// it that way. This overload lets the same emission work whether the value
    /// arrived as a CLR instant or as the raw count.
    /// </summary>
    public static long Seconds(long value) => value;

    /// <summary>A datetime from seconds since the MQL5 epoch.</summary>
    public static System.DateTime Moment(long seconds) => Epoch.AddSeconds(seconds);

    /// <summary>A datetime from a real number of seconds since the MQL5 epoch.</summary>
    public static System.DateTime Moment(double seconds) => Epoch.AddSeconds((long)seconds);

    /// <summary>
    /// MQL5 <c>+</c> concatenates as soon as either side is text. Real values follow
    /// the MQL5 implicit conversion, which prints at most eight decimal places and
    /// drops trailing zeros.
    /// </summary>
    public static string Concat(object? left, object? right) => ToText(left) + ToText(right);

    /// <summary>MQL5 parses a string to a number rather than reinterpreting it; a string that is
    /// not a number reads as zero, as StringToInteger and StringToDouble do.</summary>
    public static long ToLong(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;

    public static double ToDouble(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : 0D;

    /// <summary>The MQL5 implicit conversion of a value to text.</summary>
    public static string ToText(object? value) => value switch
    {
        null => "",
        string text => text,
        bool logical => logical ? "true" : "false",
        double real => real.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
        float real => ((double)real).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
        System.DateTime moment => moment.ToString(
            "yyyy.MM.dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        System.IFormattable formattable =>
            formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>MQL5 indexes a string to a character code; C# indexes it to a <c>char</c>.</summary>
    public static ushort CharAt(string? value, int index) =>
        value is null || index < 0 || index >= value.Length ? (ushort)0 : (ushort)value[index];

    /// <summary>
    /// MQL5 <c>delete</c> releases an object the strategy allocated with <c>new</c>.
    /// The CLR collects it instead, so the only observable part left is the disposal of
    /// anything the runtime attached to it.
    /// </summary>
    public static void Delete(object? value)
    {
        if (value is System.IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>A rectangular MQL5 array of rank two, modelled as a jagged CLR array.</summary>
    public static T[][][] NewArray3<T>(int outer, int middle, int inner)
    {
        var result = new T[outer < 0 ? 0 : outer][][];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = NewArray2<T>(middle, inner);
        }

        return result;
    }

    public static T[][] NewArray2<T>(int outer, int inner)
    {
        var result = new T[outer < 0 ? 0 : outer][];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = new T[inner < 0 ? 0 : inner];
        }

        return result;
    }
}
""";
}
