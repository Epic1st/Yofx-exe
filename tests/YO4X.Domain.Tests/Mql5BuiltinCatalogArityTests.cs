using YO4X.StrategyGovernance;

namespace YO4X.Domain.Tests;

/// <summary>
/// Arity facts about the built-in catalogue, each one measured against the MetaEditor
/// compiler shipped with MetaTrader 5 rather than read off a documentation page. The
/// refusals matter as much as the acceptances: an MQL4 arity that we decline to invent
/// an MQL5 overload for must stay declined.
/// </summary>
public sealed class Mql5BuiltinCatalogArityTests
{
    /// <summary>
    /// MetaEditor reports <c>built-in: bool SetIndexBuffer(int,double&amp;[],ENUM_INDEXBUFFER_TYPE)</c>
    /// yet compiles a two-argument call with 0 errors, so <c>data_type</c> carries a default.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SetIndexBufferAcceptsTwoOrThreeArguments(int arguments)
    {
        Assert.True(AcceptsArity("SetIndexBuffer", arguments));
    }

    [Fact]
    public void SetIndexBufferRefusesFourArguments()
    {
        Assert.False(AcceptsArity("SetIndexBuffer", 4));
    }

    /// <summary>
    /// MetaEditor reports <c>int CalendarValueHistory(MqlCalendarValue&amp;[...],datetime,datetime,const string,const string)</c>
    /// and compiles calls at two, three, four and five arguments.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CalendarValueHistoryAcceptsTwoThroughFiveArguments(int arguments)
    {
        Assert.True(AcceptsArity("CalendarValueHistory", arguments));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void CalendarValueHistoryRefusesOtherArities(int arguments)
    {
        Assert.False(AcceptsArity("CalendarValueHistory", arguments));
    }

    /// <summary>
    /// MetaEditor reports <c>built-in: bool CalendarCountryById(ulong,MqlCalendarCountry&amp;)</c>
    /// and answers a one-argument call with error 199, so both parameters are required.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void CalendarCountryByIdTakesExactlyTwoArguments(int arguments, bool accepted)
    {
        Assert.Equal(accepted, AcceptsArity("CalendarCountryById", arguments));
    }

    /// <summary>
    /// MetaEditor reports
    /// <c>built-in: int CalendarValueLast(ulong&amp;,MqlCalendarValue&amp;[...],const string,const string)</c>
    /// and compiles calls at two, three and four arguments while refusing one.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void CalendarValueLastAcceptsTwoThroughFourArguments(int arguments, bool accepted)
    {
        Assert.Equal(accepted, AcceptsArity("CalendarValueLast", arguments));
    }

    /// <summary>
    /// Shape and realisability are separate questions. The whole Calendar* family is
    /// measured, so a back end must not report that MQL5 declares no such overload; it is
    /// still classified <c>Unsupported</c>, because no calendar data source exists behind it.
    /// </summary>
    [Theory]
    [InlineData("CalendarCountryById")]
    [InlineData("CalendarEventById")]
    [InlineData("CalendarValueLast")]
    [InlineData("CalendarValueHistory")]
    public void CalendarFamilyIsMeasuredButUnsupported(string name)
    {
        Assert.True(Mql5BuiltinCatalog.TryGet(name, out IReadOnlyList<Mql5BuiltinSignature> overloads));
        Assert.All(overloads, signature => Assert.True(signature.Verified));
        Assert.All(overloads, signature => Assert.NotEmpty(signature.Parameters));
        Assert.All(
            overloads,
            signature => Assert.Equal(Mql5BuiltinSupport.Unsupported, signature.Support));
    }

    /// <summary>
    /// No catalogue entry is left with an unconfirmed parameter list. An entry with
    /// <c>Verified: false</c> asserts only that the name exists; both back ends refuse to
    /// bind one, and the Calendar* family was the last group in that state.
    /// </summary>
    [Fact]
    public void EveryCataloguedSignatureCarriesAMeasuredParameterList()
    {
        Assert.All(Mql5BuiltinCatalog.All, signature => Assert.True(signature.Verified));
    }

    /// <summary>
    /// MetaEditor answers a call to <c>CalendarEventHistory</c> with
    /// <c>error 256: undeclared identifier</c>: MQL5 has no such name, and a catalogue
    /// entry would let a binder resolve a name the language never defined.
    /// </summary>
    [Fact]
    public void CalendarEventHistoryIsNotCatalogued()
    {
        Assert.False(Mql5BuiltinCatalog.TryGet("CalendarEventHistory", out _));
    }

    /// <summary>
    /// <c>ErrorDescription</c> is written in <c>stdlib.mqh</c>, not declared by the
    /// language: MetaEditor reports it as an undeclared identifier.
    /// </summary>
    [Fact]
    public void ErrorDescriptionIsNotCatalogued()
    {
        Assert.False(Mql5BuiltinCatalog.TryGet("ErrorDescription", out _));
    }

    /// <summary>
    /// MetaEditor reports <c>built-in: void printf(const string,...)</c> and compiles a
    /// call, so <c>printf</c> is a real MQL5 name rather than a C carry-over.
    /// </summary>
    [Fact]
    public void PrintfIsCataloguedAsVariadic()
    {
        Assert.True(AcceptsArity("printf", 1));
        Assert.True(AcceptsArity("printf", 8));
        Assert.False(AcceptsArity("printf", 0));
    }

    /// <summary>
    /// The MQL5* spellings are deprecated, not withdrawn: MetaEditor reports
    /// <c>built-in: string MQL5InfoString(ENUM_MQL5_INFO_STRING)</c> and compiles a call.
    /// </summary>
    [Theory]
    [InlineData("MQL5InfoString")]
    [InlineData("MQL5InfoInteger")]
    public void DeprecatedMqlInfoSpellingsAreCatalogued(string name)
    {
        Assert.True(AcceptsArity(name, 1));
        Assert.False(AcceptsArity(name, 2));
    }

    /// <summary>
    /// MQL4 arities that no MQL5 overload accepts. MetaEditor reports
    /// <c>int iMA(const string,ENUM_TIMEFRAMES,int,int,ENUM_MA_METHOD,int)</c> and
    /// <c>bool OrderSend(const MqlTradeRequest&amp;,MqlTradeResult&amp;)</c>; inventing an
    /// overload to swallow the surplus arguments would mis-bind silently.
    /// </summary>
    [Theory]
    [InlineData("iMA", 6, true)]
    [InlineData("iMA", 7, false)]
    [InlineData("OrderSend", 2, true)]
    [InlineData("OrderSend", 11, false)]
    public void Mql4ArityStaysRefused(string name, int arguments, bool accepted)
    {
        Assert.Equal(accepted, AcceptsArity(name, arguments));
    }

    private static bool AcceptsArity(string name, int arguments) =>
        Mql5BuiltinCatalog.TryGet(name, out IReadOnlyList<Mql5BuiltinSignature> overloads)
        && overloads.Any(signature => signature.AcceptsArgumentCount(arguments));
}
