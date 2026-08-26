namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 <c>MqlCalendarEvent</c>: the description of one economic-calendar event.
///
/// Declared even though every built-in that could fill one - <c>CalendarEventById</c>,
/// <c>CalendarEventHistory</c> - is refused by this runtime, and for the same reason the refused
/// built-ins are declared rather than omitted: a strategy that merely declares the structure has
/// to reach the refusal at the calendar call, naming the missing data source, instead of failing
/// at code generation with a message about an unknown type name.
/// </summary>
/// <remarks>
/// The field set is not from memory and not from the reference, which publishes the layout in
/// prose that has drifted. MQL5 ships no header for this structure, so each field was confirmed
/// against the MetaEditor compiler: a member access on a field that does not exist is an error,
/// and passing a member to a function taking that type by reference is an error unless the types
/// match exactly - MQL5 permits no widening through a reference parameter. That second check is
/// what caught <c>country_id</c>, which is <c>ulong</c> rather than the <c>long</c> the shape of
/// the other identifiers suggests.
///
/// The <c>ENUM_CALENDAR_EVENT_*</c> fields are carried as <see cref="int"/>, as every other MQL5
/// enumeration is throughout this runtime.
/// </remarks>
public record struct Mql5CalendarEvent
{
    /// <summary>MQL5 <c>id</c>. Event identifier.</summary>
    public ulong Id { get; set; }

    /// <summary>MQL5 <c>type</c>. An <c>ENUM_CALENDAR_EVENT_TYPE</c> member.</summary>
    public int Type { get; set; }

    /// <summary>MQL5 <c>sector</c>. An <c>ENUM_CALENDAR_EVENT_SECTOR</c> member.</summary>
    public int Sector { get; set; }

    /// <summary>MQL5 <c>frequency</c>. An <c>ENUM_CALENDAR_EVENT_FREQUENCY</c> member.</summary>
    public int Frequency { get; set; }

    /// <summary>MQL5 <c>time_mode</c>. An <c>ENUM_CALENDAR_EVENT_TIMEMODE</c> member.</summary>
    public int TimeMode { get; set; }

    /// <summary>MQL5 <c>country_id</c>. Confirmed <c>ulong</c>, not <c>long</c>.</summary>
    public ulong CountryId { get; set; }

    /// <summary>MQL5 <c>unit</c>. An <c>ENUM_CALENDAR_EVENT_UNIT</c> member.</summary>
    public int Unit { get; set; }

    /// <summary>MQL5 <c>importance</c>. An <c>ENUM_CALENDAR_EVENT_IMPORTANCE</c> member.</summary>
    public int Importance { get; set; }

    /// <summary>MQL5 <c>multiplier</c>. An <c>ENUM_CALENDAR_EVENT_MULTIPLIER</c> member.</summary>
    public int Multiplier { get; set; }

    /// <summary>MQL5 <c>digits</c>. Decimal places of the event's values.</summary>
    public uint Digits { get; set; }

    /// <summary>MQL5 <c>source_url</c>.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>MQL5 <c>event_code</c>.</summary>
    public string? EventCode { get; set; }

    /// <summary>MQL5 <c>name</c>.</summary>
    public string? Name { get; set; }
}

/// <summary>
/// MQL5 <c>MqlCalendarValue</c>: one released value of an economic-calendar event.
/// </summary>
/// <remarks>
/// Confirmed field by field against the MetaEditor compiler in the same way as
/// <see cref="Mql5CalendarEvent"/>, including the eight accessor methods, which do exist on the
/// MQL5 structure and are therefore declared here rather than left to fail as an unknown member.
///
/// The accessors refuse rather than compute. MQL5 stores each figure scaled by a power of ten
/// and reports absence with a sentinel, and neither the scale nor the sentinel can be confirmed
/// with a compile-time-only oracle - the compiler will tell us the method exists and its return
/// type, and nothing about its arithmetic. Guessing the scale would turn a released figure of
/// 2.5% into 2500000 or 0.0000025 in a strategy's comparison. Every built-in that can fill this
/// structure is refused anyway, so the only instance a strategy can hold is a zeroed one, on
/// which <c>HasActualValue</c> computed against a guessed sentinel would answer "yes, and the
/// value is zero" - a false statement about data that was never read.
/// </remarks>
public record struct Mql5CalendarValue
{
    /// <summary>MQL5 <c>id</c>. Value identifier.</summary>
    public ulong Id { get; set; }

    /// <summary>MQL5 <c>event_id</c>. The <see cref="Mql5CalendarEvent.Id"/> this value belongs to.</summary>
    public ulong EventId { get; set; }

    /// <summary>MQL5 <c>time</c>. Release time, seconds since 1970-01-01 UTC.</summary>
    public long Time { get; set; }

    /// <summary>MQL5 <c>period</c>. The reporting period, seconds since 1970-01-01 UTC.</summary>
    public long Period { get; set; }

    /// <summary>MQL5 <c>revision</c>. Revision of the published indicator, relative to the period.</summary>
    public int Revision { get; set; }

    /// <summary>MQL5 <c>actual_value</c>, in the structure's own scaled form.</summary>
    public long ActualValue { get; set; }

    /// <summary>MQL5 <c>prev_value</c>, in the structure's own scaled form.</summary>
    public long PreviousValue { get; set; }

    /// <summary>MQL5 <c>revised_prev_value</c>, in the structure's own scaled form.</summary>
    public long RevisedPreviousValue { get; set; }

    /// <summary>MQL5 <c>forecast_value</c>, in the structure's own scaled form.</summary>
    public long ForecastValue { get; set; }

    /// <summary>MQL5 <c>impact_type</c>. An <c>ENUM_CALENDAR_EVENT_IMPACT</c> member.</summary>
    public int ImpactType { get; set; }

    /// <summary>MQL5 <c>HasActualValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly bool HasActualValue() => throw Refuse(nameof(HasActualValue));

    /// <summary>MQL5 <c>HasPreviousValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly bool HasPreviousValue() => throw Refuse(nameof(HasPreviousValue));

    /// <summary>MQL5 <c>HasRevisedValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly bool HasRevisedValue() => throw Refuse(nameof(HasRevisedValue));

    /// <summary>MQL5 <c>HasForecastValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly bool HasForecastValue() => throw Refuse(nameof(HasForecastValue));

    /// <summary>MQL5 <c>GetActualValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly double GetActualValue() => throw Refuse(nameof(GetActualValue));

    /// <summary>MQL5 <c>GetPreviousValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly double GetPreviousValue() => throw Refuse(nameof(GetPreviousValue));

    /// <summary>MQL5 <c>GetRevisedValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly double GetRevisedValue() => throw Refuse(nameof(GetRevisedValue));

    /// <summary>MQL5 <c>GetForecastValue</c>. Unsupported; see the type's remarks.</summary>
    public readonly double GetForecastValue() => throw Refuse(nameof(GetForecastValue));

    private static Mql5UnsupportedOperationException Refuse(string method)
        => Mql5UnsupportedOperationException.For(
            "MqlCalendarValue::" + method,
            "no economic-calendar data source is available to the engine, so this structure is "
            + "never filled, and the scaling and absent-value sentinel MQL5 applies to the raw "
            + "fields have not been measured");
}
