namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 mathematical functions. Every one is <b>Native</b>: implemented here, with no
/// market context.
///
/// Two of them are not the obvious .NET call. <c>MathRand</c> reproduces the Microsoft
/// C runtime generator MQL5 inherits, returning 0 to 32767 rather than .NET's
/// <c>Random</c> range, and it is seeded from runtime options so a backtest replays
/// identically. <c>MathMod</c> follows C's <c>fmod</c>, whose result carries the sign
/// of the dividend, and not <see cref="Math.IEEERemainder(double, double)"/>, which
/// rounds the quotient to even and gets -1 where MQL5 gets 1.
///
/// MQL5 documents a C-style alias for almost every one of these; the corpus calls
/// <c>fabs</c>, <c>fmin</c>, <c>fmax</c>, <c>fmod</c> and <c>round</c> directly, so the
/// aliases are declared under their MQL5 spelling rather than left to the binder.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>MathAbs</c>. Native.</summary>
    double MathAbs(double value);

    /// <summary>MQL5 <c>MathAbs</c> over an integer, where MQL5 keeps the integer type. Native.</summary>
    long MathAbs(long value);

    /// <summary>MQL5 <c>MathMax</c>. Native.</summary>
    double MathMax(double first, double second);

    /// <summary>MQL5 <c>MathMax</c> over integers, where the wider operand type wins. Native.</summary>
    long MathMax(long first, long second);

    /// <summary>MQL5 <c>MathMin</c>. Native.</summary>
    double MathMin(double first, double second);

    /// <summary>MQL5 <c>MathMin</c> over integers, where the wider operand type wins. Native.</summary>
    long MathMin(long first, long second);

    /// <summary>MQL5 <c>MathFloor</c>. Native.</summary>
    double MathFloor(double value);

    /// <summary>MQL5 <c>MathCeil</c>. Native.</summary>
    double MathCeil(double value);

    /// <summary>MQL5 <c>MathRound</c>. Rounds half away from zero, as C's <c>round</c> does. Native.</summary>
    double MathRound(double value);

    /// <summary>MQL5 <c>MathPow</c>. Native.</summary>
    double MathPow(double baseValue, double exponent);

    /// <summary>MQL5 <c>MathSqrt</c>. Native.</summary>
    double MathSqrt(double value);

    /// <summary>MQL5 <c>MathExp</c>. Native.</summary>
    double MathExp(double value);

    /// <summary>MQL5 <c>MathLog</c>, the natural logarithm. Native.</summary>
    double MathLog(double value);

    /// <summary>MQL5 <c>MathLog10</c>. Native.</summary>
    double MathLog10(double value);

    /// <summary>
    /// MQL5 <c>MathMod</c>. C <c>fmod</c> semantics: the result carries the sign of
    /// <paramref name="value"/>, so <c>MathMod(-7, 3)</c> is -1, not 2. Native.
    /// </summary>
    double MathMod(double value, double divisor);

    /// <summary>
    /// MQL5 <c>MathRand</c>. Returns 0 to 32767 inclusive from the Microsoft C runtime
    /// generator, seeded deterministically. Native.
    /// </summary>
    int MathRand();

    /// <summary>MQL5 <c>MathSrand</c>. Reseeds <see cref="MathRand"/>. Native.</summary>
    void MathSrand(int seed);

    /// <summary>MQL5 <c>MathSin</c>. Native.</summary>
    double MathSin(double value);

    /// <summary>MQL5 <c>MathCos</c>. Native.</summary>
    double MathCos(double value);

    /// <summary>MQL5 <c>MathTan</c>. Native.</summary>
    double MathTan(double radians);

    /// <summary>MQL5 <c>MathArcsin</c>. Native.</summary>
    double MathArcsin(double value);

    /// <summary>MQL5 <c>MathArccos</c>. Native.</summary>
    double MathArccos(double value);

    /// <summary>MQL5 <c>MathArctan</c>. Native.</summary>
    double MathArctan(double value);

    /// <summary>MQL5 <c>MathArctan2</c>. Native.</summary>
    double MathArctan2(double y, double x);

    /// <summary>MQL5 <c>MathSinh</c>. Native.</summary>
    double MathSinh(double value);

    /// <summary>MQL5 <c>MathCosh</c>. Native.</summary>
    double MathCosh(double value);

    /// <summary>MQL5 <c>MathTanh</c>. Native.</summary>
    double MathTanh(double value);

    /// <summary>MQL5 <c>MathArcsinh</c>. Native.</summary>
    double MathArcsinh(double value);

    /// <summary>MQL5 <c>MathArccosh</c>. Native.</summary>
    double MathArccosh(double value);

    /// <summary>MQL5 <c>MathArctanh</c>. Native.</summary>
    double MathArctanh(double value);

    /// <summary>MQL5 <c>MathExpm1</c>, accurate for small arguments. Native.</summary>
    double MathExpm1(double value);

    /// <summary>MQL5 <c>MathLog1p</c>, accurate for small arguments. Native.</summary>
    double MathLog1p(double value);

    /// <summary>MQL5 <c>MathIsValidNumber</c>: false for NaN and both infinities. Native.</summary>
    bool MathIsValidNumber(double number);

    /// <summary>MQL5 <c>MathSwap</c>: reverses the byte order of a 16-bit value. Native.</summary>
    ushort MathSwap(ushort value);

    /// <summary>MQL5 <c>MathSwap</c>: reverses the byte order of a 32-bit value. Native.</summary>
    uint MathSwap(uint value);

    /// <summary>MQL5 <c>MathSwap</c>: reverses the byte order of a 64-bit value. Native.</summary>
    ulong MathSwap(ulong value);

    // MQL5 documents each of the following as an alias of the Math function above it.
    // They are declared rather than folded away because the corpus calls them.

    /// <summary>MQL5 <c>fabs</c>, the documented alias of <see cref="MathAbs(double)"/>. Native.</summary>
    double Fabs(double value);

    /// <summary>MQL5 <c>fmax</c>, the documented alias of <see cref="MathMax(double, double)"/>. Native.</summary>
    double Fmax(double first, double second);

    /// <summary>MQL5 <c>fmin</c>, the documented alias of <see cref="MathMin(double, double)"/>. Native.</summary>
    double Fmin(double first, double second);

    /// <summary>MQL5 <c>fmod</c>, the documented alias of <see cref="MathMod"/>. Native.</summary>
    double Fmod(double value, double divisor);

    /// <summary>MQL5 <c>pow</c>, the documented alias of <see cref="MathPow"/>. Native.</summary>
    double Pow(double baseValue, double exponent);

    /// <summary>MQL5 <c>sqrt</c>, the documented alias of <see cref="MathSqrt"/>. Native.</summary>
    double Sqrt(double value);

    /// <summary>MQL5 <c>floor</c>, the documented alias of <see cref="MathFloor"/>. Native.</summary>
    double Floor(double value);

    /// <summary>MQL5 <c>ceil</c>, the documented alias of <see cref="MathCeil"/>. Native.</summary>
    double Ceil(double value);

    /// <summary>MQL5 <c>round</c>, the documented alias of <see cref="MathRound"/>. Native.</summary>
    double Round(double value);

    /// <summary>MQL5 <c>exp</c>, the documented alias of <see cref="MathExp"/>. Native.</summary>
    double Exp(double value);

    /// <summary>MQL5 <c>log</c>, the documented alias of <see cref="MathLog"/>. Native.</summary>
    double Log(double value);

    /// <summary>MQL5 <c>log10</c>, the documented alias of <see cref="MathLog10"/>. Native.</summary>
    double Log10(double value);

    /// <summary>MQL5 <c>rand</c>, the documented alias of <see cref="MathRand"/>. Native.</summary>
    int Rand();

    /// <summary>MQL5 <c>srand</c>, the documented alias of <see cref="MathSrand"/>. Native.</summary>
    void Srand(int seed);

    /// <summary>MQL5 <c>sin</c>, the documented alias of <see cref="MathSin"/>. Native.</summary>
    double Sin(double value);

    /// <summary>MQL5 <c>cos</c>, the documented alias of <see cref="MathCos"/>. Native.</summary>
    double Cos(double value);

    /// <summary>MQL5 <c>tan</c>, the documented alias of <see cref="MathTan"/>. Native.</summary>
    double Tan(double radians);

    /// <summary>MQL5 <c>asin</c>, the documented alias of <see cref="MathArcsin"/>. Native.</summary>
    double Asin(double value);

    /// <summary>MQL5 <c>acos</c>, the documented alias of <see cref="MathArccos"/>. Native.</summary>
    double Acos(double value);

    /// <summary>MQL5 <c>atan</c>, the documented alias of <see cref="MathArctan"/>. Native.</summary>
    double Atan(double value);

    /// <summary>MQL5 <c>atan2</c>, the documented alias of <see cref="MathArctan2"/>. Native.</summary>
    double Atan2(double y, double x);

    /// <summary>MQL5 <c>sinh</c>, the documented alias of <see cref="MathSinh"/>. Native.</summary>
    double Sinh(double value);

    /// <summary>MQL5 <c>cosh</c>, the documented alias of <see cref="MathCosh"/>. Native.</summary>
    double Cosh(double value);

    /// <summary>MQL5 <c>tanh</c>, the documented alias of <see cref="MathTanh"/>. Native.</summary>
    double Tanh(double value);

    /// <summary>MQL5 <c>asinh</c>, the documented alias of <see cref="MathArcsinh"/>. Native.</summary>
    double Asinh(double value);

    /// <summary>MQL5 <c>acosh</c>, the documented alias of <see cref="MathArccosh"/>. Native.</summary>
    double Acosh(double value);

    /// <summary>MQL5 <c>atanh</c>, the documented alias of <see cref="MathArctanh"/>. Native.</summary>
    double Atanh(double value);

    /// <summary>MQL5 <c>expm1</c>, the documented alias of <see cref="MathExpm1"/>. Native.</summary>
    double Expm1(double value);

    /// <summary>MQL5 <c>log1p</c>, the documented alias of <see cref="MathLog1p"/>. Native.</summary>
    double Log1p(double value);
}

public sealed partial class Mql5Runtime
{
    // The Microsoft C runtime linear congruential generator, which MQL5 inherits.
    // Reproduced rather than replaced by System.Random so that a strategy seeded with
    // MathSrand(12345) produces the same sequence here as it does on a terminal.
    private const uint RandMultiplier = 214013;
    private const uint RandIncrement = 2531011;

    /// <inheritdoc />
    public double MathAbs(double value) => Math.Abs(value);

    /// <inheritdoc />
    public long MathAbs(long value) => value == long.MinValue ? long.MinValue : Math.Abs(value);

    /// <inheritdoc />
    public double MathMax(double first, double second) => Math.Max(first, second);

    /// <inheritdoc />
    public long MathMax(long first, long second) => Math.Max(first, second);

    /// <inheritdoc />
    public double MathMin(double first, double second) => Math.Min(first, second);

    /// <inheritdoc />
    public long MathMin(long first, long second) => Math.Min(first, second);

    /// <inheritdoc />
    public double MathFloor(double value) => Math.Floor(value);

    /// <inheritdoc />
    public double MathCeil(double value) => Math.Ceiling(value);

    /// <inheritdoc />
    public double MathRound(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <inheritdoc />
    public double MathPow(double baseValue, double exponent) => Math.Pow(baseValue, exponent);

    /// <inheritdoc />
    public double MathSqrt(double value) => Math.Sqrt(value);

    /// <inheritdoc />
    public double MathExp(double value) => Math.Exp(value);

    /// <inheritdoc />
    public double MathLog(double value) => Math.Log(value);

    /// <inheritdoc />
    public double MathLog10(double value) => Math.Log10(value);

    /// <inheritdoc />
    public double MathMod(double value, double divisor) => value % divisor;

    /// <inheritdoc />
    public int MathRand()
    {
        randomState = unchecked((randomState * RandMultiplier) + RandIncrement);
        return (int)((randomState >> 16) & 0x7FFFu);
    }

    /// <inheritdoc />
    public void MathSrand(int seed) => randomState = unchecked((uint)seed);

    /// <inheritdoc />
    public double MathSin(double value) => Math.Sin(value);

    /// <inheritdoc />
    public double MathCos(double value) => Math.Cos(value);

    /// <inheritdoc />
    public double MathTan(double radians) => Math.Tan(radians);

    /// <inheritdoc />
    public double MathArcsin(double value) => Math.Asin(value);

    /// <inheritdoc />
    public double MathArccos(double value) => Math.Acos(value);

    /// <inheritdoc />
    public double MathArctan(double value) => Math.Atan(value);

    /// <inheritdoc />
    public double MathArctan2(double y, double x) => Math.Atan2(y, x);

    /// <inheritdoc />
    public double MathSinh(double value) => Math.Sinh(value);

    /// <inheritdoc />
    public double MathCosh(double value) => Math.Cosh(value);

    /// <inheritdoc />
    public double MathTanh(double value) => Math.Tanh(value);

    /// <inheritdoc />
    public double MathArcsinh(double value) => Math.Asinh(value);

    /// <inheritdoc />
    public double MathArccosh(double value) => Math.Acosh(value);

    /// <inheritdoc />
    public double MathArctanh(double value) => Math.Atanh(value);

    /// <inheritdoc />
    public double MathExpm1(double value) => Exp1M(value);

    /// <inheritdoc />
    public double MathLog1p(double value) => Log1PCore(value);

    /// <inheritdoc />
    public bool MathIsValidNumber(double number) => !double.IsNaN(number) && !double.IsInfinity(number);

    /// <inheritdoc />
    public ushort MathSwap(ushort value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    /// <inheritdoc />
    public uint MathSwap(uint value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    /// <inheritdoc />
    public ulong MathSwap(ulong value) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);

    /// <inheritdoc />
    public double Fabs(double value) => MathAbs(value);

    /// <inheritdoc />
    public double Fmax(double first, double second) => MathMax(first, second);

    /// <inheritdoc />
    public double Fmin(double first, double second) => MathMin(first, second);

    /// <inheritdoc />
    public double Fmod(double value, double divisor) => MathMod(value, divisor);

    /// <inheritdoc />
    public double Pow(double baseValue, double exponent) => MathPow(baseValue, exponent);

    /// <inheritdoc />
    public double Sqrt(double value) => MathSqrt(value);

    /// <inheritdoc />
    public double Floor(double value) => MathFloor(value);

    /// <inheritdoc />
    public double Ceil(double value) => MathCeil(value);

    /// <inheritdoc />
    public double Round(double value) => MathRound(value);

    /// <inheritdoc />
    public double Exp(double value) => MathExp(value);

    /// <inheritdoc />
    public double Log(double value) => MathLog(value);

    /// <inheritdoc />
    public double Log10(double value) => MathLog10(value);

    /// <inheritdoc />
    public int Rand() => MathRand();

    /// <inheritdoc />
    public void Srand(int seed) => MathSrand(seed);

    /// <inheritdoc />
    public double Sin(double value) => MathSin(value);

    /// <inheritdoc />
    public double Cos(double value) => MathCos(value);

    /// <inheritdoc />
    public double Tan(double radians) => MathTan(radians);

    /// <inheritdoc />
    public double Asin(double value) => MathArcsin(value);

    /// <inheritdoc />
    public double Acos(double value) => MathArccos(value);

    /// <inheritdoc />
    public double Atan(double value) => MathArctan(value);

    /// <inheritdoc />
    public double Atan2(double y, double x) => MathArctan2(y, x);

    /// <inheritdoc />
    public double Sinh(double value) => MathSinh(value);

    /// <inheritdoc />
    public double Cosh(double value) => MathCosh(value);

    /// <inheritdoc />
    public double Tanh(double value) => MathTanh(value);

    /// <inheritdoc />
    public double Asinh(double value) => MathArcsinh(value);

    /// <inheritdoc />
    public double Acosh(double value) => MathArccosh(value);

    /// <inheritdoc />
    public double Atanh(double value) => MathArctanh(value);

    /// <inheritdoc />
    public double Expm1(double value) => MathExpm1(value);

    /// <inheritdoc />
    public double Log1p(double value) => MathLog1p(value);

    // exp(x) - 1 computed so that the leading 1 does not cancel the whole result for
    // small x, which is the only reason MQL5 documents expm1 separately from exp.
    private static double Exp1M(double value)
    {
        double raised = Math.Exp(value);
        if (raised == 1.0)
        {
            return value;
        }

        if (raised - 1.0 == -1.0)
        {
            return -1.0;
        }

        return (raised - 1.0) * value / Math.Log(raised);
    }

    // log(1 + x), likewise arranged so that adding 1 does not lose the low bits of a
    // small x.
    private static double Log1PCore(double value)
    {
        double sum = 1.0 + value;
        if (sum == 1.0)
        {
            return value;
        }

        return Math.Log(sum) * value / (sum - 1.0);
    }
}
