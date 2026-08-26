using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The economic-calendar structures. There is nothing to test about a field, so what is pinned
/// here is the part that could rot into a lie: the accessors on
/// <see cref="Mql5CalendarValue"/> refuse rather than compute a figure from a scale nobody has
/// measured, and the widths of the two fields where the obvious guess is wrong -
/// <c>country_id</c> is <c>ulong</c>, <c>digits</c> is <c>uint</c> - stay as the MQL5 compiler
/// confirmed them.
/// </summary>
public sealed class Mql5CalendarTypesTests
{
    [Fact]
    public void EventFieldWidthsMatchTheCompiler()
    {
        Assert.Equal(typeof(ulong), typeof(Mql5CalendarEvent).GetProperty(nameof(Mql5CalendarEvent.Id))!.PropertyType);
        Assert.Equal(typeof(ulong), typeof(Mql5CalendarEvent).GetProperty(nameof(Mql5CalendarEvent.CountryId))!.PropertyType);
        Assert.Equal(typeof(uint), typeof(Mql5CalendarEvent).GetProperty(nameof(Mql5CalendarEvent.Digits))!.PropertyType);
    }

    [Fact]
    public void ValueFieldWidthsMatchTheCompiler()
    {
        Assert.Equal(typeof(ulong), typeof(Mql5CalendarValue).GetProperty(nameof(Mql5CalendarValue.EventId))!.PropertyType);
        Assert.Equal(typeof(int), typeof(Mql5CalendarValue).GetProperty(nameof(Mql5CalendarValue.Revision))!.PropertyType);
        Assert.Equal(typeof(long), typeof(Mql5CalendarValue).GetProperty(nameof(Mql5CalendarValue.ActualValue))!.PropertyType);
    }

    [Fact]
    public void AccessorsRefuseInsteadOfReportingAZeroedStructAsRealData()
    {
        Mql5CalendarValue value = default;

        Assert.Throws<Mql5UnsupportedOperationException>(() => value.HasActualValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.HasPreviousValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.HasRevisedValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.HasForecastValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.GetActualValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.GetPreviousValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.GetRevisedValue());
        Assert.Throws<Mql5UnsupportedOperationException>(() => value.GetForecastValue());
    }

    [Fact]
    public void RefusalNamesTheStructureAndTheRealReason()
    {
        Mql5CalendarValue value = default;

        Mql5UnsupportedOperationException failure =
            Assert.Throws<Mql5UnsupportedOperationException>(() => value.GetActualValue());

        Assert.Equal("MqlCalendarValue::GetActualValue", failure.FunctionName);
        Assert.Contains("no economic-calendar data source", failure.Message, StringComparison.Ordinal);
    }
}
