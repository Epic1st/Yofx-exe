using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The <c>Math*</c> family against MQL5's documented behaviour.
///
/// Two of these are not "obviously right by construction" and are the reason the file
/// exists: <c>MathMod</c> must follow C's <c>fmod</c> rather than
/// <see cref="Math.IEEERemainder(double, double)"/>, and <c>MathRand</c> must reproduce
/// the Microsoft C runtime generator MQL5 inherits - range, sequence and reseeding.
/// </summary>
public sealed class Mql5MathTests
{
    private static Mql5Runtime Build(uint seed = 1)
        => new(new FakeMarketContext(), new Mql5RuntimeOptions { RandomSeed = seed });

    [Fact]
    public void MathAbsHandlesDoubleAndInteger()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(3.5, runtime.MathAbs(-3.5));
        Assert.Equal(3.5, runtime.MathAbs(3.5));
        Assert.Equal(7L, runtime.MathAbs(-7L));
    }

    [Fact]
    public void MathMaxAndMinPickTheRightOperand()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(9.5, runtime.MathMax(2.5, 9.5));
        Assert.Equal(2.5, runtime.MathMin(2.5, 9.5));
        Assert.Equal(9L, runtime.MathMax(2L, 9L));
        Assert.Equal(2L, runtime.MathMin(2L, 9L));
    }

    [Fact]
    public void MathModFollowsFmodAndNotIeeeRemainder()
    {
        Mql5Runtime runtime = Build();

        // fmod keeps the sign of the dividend. IEEERemainder would answer -1 here.
        Assert.Equal(1.0, runtime.MathMod(7.0, 3.0));
        Assert.Equal(-1.0, runtime.MathMod(-7.0, 3.0));
        Assert.Equal(1.0, runtime.MathMod(7.0, -3.0));
        Assert.Equal(0.5, runtime.MathMod(5.5, 2.5), 12);
        // fmod(5, 3) is 2; IEEERemainder rounds the quotient to even and answers -1.
        Assert.Equal(2.0, runtime.MathMod(5.0, 3.0));
        Assert.NotEqual(Math.IEEERemainder(5.0, 3.0), runtime.MathMod(5.0, 3.0));
    }

    [Fact]
    public void MathModByZeroIsNotANumber()
    {
        Mql5Runtime runtime = Build();
        Assert.True(double.IsNaN(runtime.MathMod(5.0, 0.0)));
    }

    [Fact]
    public void MathRoundGoesAwayFromZeroOnTies()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(3.0, runtime.MathRound(2.5));
        Assert.Equal(-3.0, runtime.MathRound(-2.5));
        Assert.Equal(2.0, runtime.MathRound(2.4));
        Assert.Equal(4.0, runtime.MathRound(3.5));
    }

    [Fact]
    public void MathFloorAndCeilRoundTowardsTheRightInfinity()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(2.0, runtime.MathFloor(2.9));
        Assert.Equal(-3.0, runtime.MathFloor(-2.1));
        Assert.Equal(3.0, runtime.MathCeil(2.1));
        Assert.Equal(-2.0, runtime.MathCeil(-2.9));
    }

    [Fact]
    public void MathRandStaysInsideTheDocumentedRange()
    {
        Mql5Runtime runtime = Build();

        for (int index = 0; index < 5000; index++)
        {
            int value = runtime.MathRand();
            Assert.InRange(value, 0, Mql5Constants.RandMax);
        }
    }

    [Fact]
    public void MathRandIsDeterministicForAGivenSeed()
    {
        Mql5Runtime first = Build(seed: 12345);
        Mql5Runtime second = Build(seed: 12345);

        for (int index = 0; index < 200; index++)
        {
            Assert.Equal(first.MathRand(), second.MathRand());
        }
    }

    [Fact]
    public void MathRandDiffersBetweenSeeds()
    {
        Mql5Runtime first = Build(seed: 1);
        Mql5Runtime second = Build(seed: 2);

        List<int> left = [.. Enumerable.Range(0, 20).Select(_ => first.MathRand())];
        List<int> right = [.. Enumerable.Range(0, 20).Select(_ => second.MathRand())];

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void MathRandReproducesTheMicrosoftCRuntimeSequence()
    {
        // srand(1) followed by rand() four times on the Microsoft C runtime, which is
        // the generator MQL5 exposes as MathSrand/MathRand.
        Mql5Runtime runtime = Build(seed: 1);

        Assert.Equal(41, runtime.MathRand());
        Assert.Equal(18467, runtime.MathRand());
        Assert.Equal(6334, runtime.MathRand());
        Assert.Equal(26500, runtime.MathRand());
    }

    [Fact]
    public void MathSrandRestartsTheSequence()
    {
        Mql5Runtime runtime = Build(seed: 99);

        runtime.MathSrand(7);
        List<int> first = [.. Enumerable.Range(0, 10).Select(_ => runtime.MathRand())];

        runtime.MathSrand(7);
        List<int> second = [.. Enumerable.Range(0, 10).Select(_ => runtime.MathRand())];

        Assert.Equal(first, second);
    }

    [Fact]
    public void PowSqrtLogAndExpMatchTheirDefinitions()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(8.0, runtime.MathPow(2.0, 3.0), 12);
        Assert.Equal(3.0, runtime.MathSqrt(9.0), 12);
        Assert.Equal(1.0, runtime.MathLog(Math.E), 12);
        Assert.Equal(2.0, runtime.MathLog10(100.0), 12);
        Assert.Equal(Math.E, runtime.MathExp(1.0), 12);
    }

    [Fact]
    public void TrigonometryRoundTrips()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(0.0, runtime.MathSin(0.0), 12);
        Assert.Equal(1.0, runtime.MathCos(0.0), 12);
        Assert.Equal(0.0, runtime.MathTan(0.0), 12);
        Assert.Equal(Math.PI / 2, runtime.MathArcsin(1.0), 12);
        Assert.Equal(0.0, runtime.MathArccos(1.0), 12);
        Assert.Equal(Math.PI / 4, runtime.MathArctan(1.0), 12);
        Assert.Equal(Math.PI / 4, runtime.MathArctan2(1.0, 1.0), 12);
    }

    [Fact]
    public void Expm1AndLog1pKeepPrecisionForSmallArguments()
    {
        Mql5Runtime runtime = Build();

        const double tiny = 1e-12;
        Assert.Equal(tiny, runtime.MathExpm1(tiny), 15);
        Assert.Equal(tiny, runtime.MathLog1p(tiny), 15);
        Assert.Equal(0.0, runtime.MathExpm1(0.0), 12);
        Assert.Equal(0.0, runtime.MathLog1p(0.0), 12);
    }

    [Fact]
    public void MathIsValidNumberRejectsNanAndInfinity()
    {
        Mql5Runtime runtime = Build();

        Assert.True(runtime.MathIsValidNumber(1.5));
        Assert.False(runtime.MathIsValidNumber(double.NaN));
        Assert.False(runtime.MathIsValidNumber(double.PositiveInfinity));
        Assert.False(runtime.MathIsValidNumber(double.NegativeInfinity));
    }

    [Fact]
    public void MathSwapReversesByteOrder()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal((ushort)0x3412, runtime.MathSwap((ushort)0x1234));
        Assert.Equal(0x78563412u, runtime.MathSwap(0x12345678u));
        Assert.Equal(0xEFCDAB8967452301UL, runtime.MathSwap(0x0123456789ABCDEFUL));
    }

    [Fact]
    public void DocumentedCStyleAliasesAgreeWithTheirOrigins()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(runtime.MathAbs(-2.5), runtime.Fabs(-2.5));
        Assert.Equal(runtime.MathMax(1.0, 2.0), runtime.Fmax(1.0, 2.0));
        Assert.Equal(runtime.MathMin(1.0, 2.0), runtime.Fmin(1.0, 2.0));
        Assert.Equal(runtime.MathMod(7.0, 3.0), runtime.Fmod(7.0, 3.0));
        Assert.Equal(runtime.MathPow(2.0, 5.0), runtime.Pow(2.0, 5.0));
        Assert.Equal(runtime.MathSqrt(16.0), runtime.Sqrt(16.0));
        Assert.Equal(runtime.MathFloor(1.9), runtime.Floor(1.9));
        Assert.Equal(runtime.MathCeil(1.1), runtime.Ceil(1.1));
        Assert.Equal(runtime.MathRound(1.5), runtime.Round(1.5));
    }
}
