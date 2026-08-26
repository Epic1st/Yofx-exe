using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// <see cref="Mql5TypeInfo.Mql5TypeName(Type)"/> against the spellings the MQL5 compiler
/// produces for <c>typename</c>.
///
/// The cases that earn their place here are the ones where the CLR would answer differently
/// from MQL5 if the mapping were derived rather than tabulated: <c>sbyte</c> is MQL5's
/// <c>char</c> and <c>byte</c> its <c>uchar</c>, the runtime's <c>Mql5TradeRequest</c> class is
/// an MQL5 <c>struct</c>, and an enumeration is a CLR value type but never an MQL5 struct. The
/// handle form is here for the space before the star, which is easy to lose in an edit and
/// invisible in review.
/// </summary>
public sealed class Mql5TypeInfoTests
{
    private struct SampleStruct;

    private sealed class SampleClass;

    private enum SampleEnum
    {
        None = 0,
    }

    [Theory]
    [InlineData(typeof(void), "void")]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(sbyte), "char")]
    [InlineData(typeof(byte), "uchar")]
    [InlineData(typeof(short), "short")]
    [InlineData(typeof(ushort), "ushort")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(uint), "uint")]
    [InlineData(typeof(long), "long")]
    [InlineData(typeof(ulong), "ulong")]
    [InlineData(typeof(float), "float")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(string), "string")]
    public void ScalarsSpellThemselves(Type type, string expected)
        => Assert.Equal(expected, Mql5TypeInfo.Mql5TypeName(type));

    [Fact]
    public void DatetimeAndColourCollapseOntoTheirIntegerRepresentation()
    {
        // Not an oversight being pinned: this toolchain carries `datetime` as `long` and `color`
        // as `int`, so nothing distinguishes them at runtime. The test records the consequence so
        // that a future change to the representation shows up here rather than in a strategy.
        Assert.Equal("long", Mql5TypeInfo.Mql5TypeName(typeof(long)));
        Assert.Equal("int", Mql5TypeInfo.Mql5TypeName(typeof(int)));
    }

    [Fact]
    public void UserDeclaredTypesTakeTheirKeyword()
    {
        Assert.Equal("struct SampleStruct", Mql5TypeInfo.Mql5TypeName(typeof(SampleStruct)));
        Assert.Equal("class SampleClass", Mql5TypeInfo.Mql5TypeName(typeof(SampleClass)));
        Assert.Equal("enum SampleEnum", Mql5TypeInfo.Mql5TypeName(typeof(SampleEnum)));
    }

    [Fact]
    public void HandleAddsASpaceBeforeTheStar()
    {
        Assert.Equal("class SampleClass *", Mql5TypeInfo.Mql5TypeName(typeof(SampleClass), isHandle: true));
        Assert.Equal("int *", Mql5TypeInfo.Mql5TypeName(typeof(int), isHandle: true));
    }

    [Fact]
    public void RuntimeStructuresKeepTheirMql5NameAndKeyword()
    {
        // Mql5TradeRequest is a CLR class and an MQL5 struct; deriving the keyword from
        // Type.IsValueType would print "class MqlTradeRequest", which no MQL5 compiler emits.
        Assert.Equal("struct MqlTradeRequest", Mql5TypeInfo.Mql5TypeName(typeof(Mql5TradeRequest)));
        Assert.Equal("struct MqlTradeResult", Mql5TypeInfo.Mql5TypeName(typeof(Mql5TradeResult)));
        Assert.Equal("struct MqlTick", Mql5TypeInfo.Mql5TypeName(typeof(Mql5Tick)));
        Assert.Equal("struct MqlRates", Mql5TypeInfo.Mql5TypeName(typeof(Mql5Rates)));
        Assert.Equal("struct MqlDateTime", Mql5TypeInfo.Mql5TypeName(typeof(Mql5DateTime)));
        Assert.Equal("struct MqlCalendarEvent", Mql5TypeInfo.Mql5TypeName(typeof(Mql5CalendarEvent)));
        Assert.Equal("struct MqlCalendarValue", Mql5TypeInfo.Mql5TypeName(typeof(Mql5CalendarValue)));
    }

    [Fact]
    public void StandardLibraryClassesKeepTheirMql5Name()
    {
        Assert.Equal("class CTrade", Mql5TypeInfo.Mql5TypeName(typeof(Mql5Trade)));
        Assert.Equal("class CPositionInfo", Mql5TypeInfo.Mql5TypeName(typeof(Mql5PositionInfo)));
        Assert.Equal("class CSymbolInfo *", Mql5TypeInfo.Mql5TypeName(typeof(Mql5SymbolInfo), isHandle: true));
    }

    [Fact]
    public void ByReferenceUnwrapsToTheReferent()
        => Assert.Equal("double", Mql5TypeInfo.Mql5TypeName(typeof(double).MakeByRefType()));

    [Theory]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(char))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(object))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(List<int>))]
    public void UnmeasuredSpellingsRefuseByName(Type type)
    {
        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => Mql5TypeInfo.Mql5TypeName(type));

        Assert.Equal("typename", failure.FunctionName);
        Assert.Contains(type.Name, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullTypeIsARejectedArgumentRatherThanARefusal()
        => Assert.Throws<ArgumentNullException>(() => Mql5TypeInfo.Mql5TypeName(null!));
}
