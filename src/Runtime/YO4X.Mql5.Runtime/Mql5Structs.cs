namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 <c>MqlDateTime</c>: a broken-down calendar date and time.
///
/// Field-for-field the MQL5 structure; the MQL5 spelling of each field is given in
/// its documentation comment. Kept mutable because <c>TimeToStruct</c> and
/// <c>TimeCurrent</c> fill it through an out reference.
/// </summary>
public record struct Mql5DateTime
{
    /// <summary>MQL5 <c>year</c>. Full four-digit year.</summary>
    public int Year { get; set; }

    /// <summary>MQL5 <c>mon</c>. Month, 1 to 12.</summary>
    public int Month { get; set; }

    /// <summary>MQL5 <c>day</c>. Day of month, 1 to 31.</summary>
    public int Day { get; set; }

    /// <summary>MQL5 <c>hour</c>. Hour, 0 to 23.</summary>
    public int Hour { get; set; }

    /// <summary>MQL5 <c>min</c>. Minute, 0 to 59.</summary>
    public int Minute { get; set; }

    /// <summary>MQL5 <c>sec</c>. Second, 0 to 59.</summary>
    public int Second { get; set; }

    /// <summary>MQL5 <c>day_of_week</c>. 0 is Sunday, as in MQL5, not as in .NET.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>MQL5 <c>day_of_year</c>. 1 for 1 January, as in MQL5, which is 0-based nowhere.</summary>
    public int DayOfYear { get; set; }
}

/// <summary>MQL5 <c>MqlTick</c>: one price update.</summary>
public record struct Mql5Tick
{
    /// <summary>MQL5 <c>time</c>. Seconds since 1970-01-01 UTC.</summary>
    public long Time { get; set; }

    /// <summary>MQL5 <c>bid</c>.</summary>
    public double Bid { get; set; }

    /// <summary>MQL5 <c>ask</c>.</summary>
    public double Ask { get; set; }

    /// <summary>MQL5 <c>last</c>. Last deal price.</summary>
    public double Last { get; set; }

    /// <summary>MQL5 <c>volume</c>. Volume for the current last price.</summary>
    public ulong Volume { get; set; }

    /// <summary>MQL5 <c>time_msc</c>. Milliseconds since 1970-01-01 UTC.</summary>
    public long TimeMsc { get; set; }

    /// <summary>MQL5 <c>flags</c>. <c>TICK_FLAG_*</c> bitmask.</summary>
    public uint Flags { get; set; }

    /// <summary>MQL5 <c>volume_real</c>. Volume with greater precision.</summary>
    public double VolumeReal { get; set; }
}

/// <summary>MQL5 <c>MqlRates</c>: one bar of a price series.</summary>
public record struct Mql5Rates
{
    /// <summary>MQL5 <c>time</c>. Bar open time, seconds since 1970-01-01 UTC.</summary>
    public long Time { get; set; }

    /// <summary>MQL5 <c>open</c>.</summary>
    public double Open { get; set; }

    /// <summary>MQL5 <c>high</c>.</summary>
    public double High { get; set; }

    /// <summary>MQL5 <c>low</c>.</summary>
    public double Low { get; set; }

    /// <summary>MQL5 <c>close</c>.</summary>
    public double Close { get; set; }

    /// <summary>MQL5 <c>tick_volume</c>.</summary>
    public long TickVolume { get; set; }

    /// <summary>MQL5 <c>spread</c>, in points.</summary>
    public int Spread { get; set; }

    /// <summary>MQL5 <c>real_volume</c>.</summary>
    public long RealVolume { get; set; }
}

/// <summary>MQL5 <c>MqlParam</c>: one argument of an <c>IndicatorCreate</c> parameter array.</summary>
public record struct Mql5Param
{
    /// <summary>MQL5 <c>type</c>. An <c>ENUM_DATATYPE</c> member.</summary>
    public int Type { get; set; }

    /// <summary>MQL5 <c>integer_value</c>.</summary>
    public long IntegerValue { get; set; }

    /// <summary>MQL5 <c>double_value</c>.</summary>
    public double DoubleValue { get; set; }

    /// <summary>MQL5 <c>string_value</c>.</summary>
    public string? StringValue { get; set; }
}

/// <summary>MQL5 <c>MqlBookInfo</c>: one level of the depth of market.</summary>
public record struct Mql5BookInfo
{
    /// <summary>MQL5 <c>type</c>. An <c>ENUM_BOOK_TYPE</c> member.</summary>
    public int Type { get; set; }

    /// <summary>MQL5 <c>price</c>.</summary>
    public double Price { get; set; }

    /// <summary>MQL5 <c>volume</c>.</summary>
    public long Volume { get; set; }

    /// <summary>MQL5 <c>volume_real</c>.</summary>
    public double VolumeReal { get; set; }
}

/// <summary>
/// Conversion between the MQL5 <c>datetime</c> scalar - seconds elapsed since
/// 1970-01-01 00:00:00 - and <see cref="DateTime"/>.
///
/// The runtime exposes <c>datetime</c> as <see cref="long"/> throughout its built-in
/// surface because MQL5 does: the corpus adds, subtracts and compares datetimes as
/// integers, and modelling them as <see cref="DateTime"/> would break that
/// arithmetic. <see cref="IMql5MarketContext"/> speaks <see cref="DateTime"/> at its
/// boundary, and these helpers are the seam.
/// </summary>
public static class Mql5Time
{
    /// <summary>1970-01-01 00:00:00 UTC, the MQL5 datetime epoch.</summary>
    public static DateTime Epoch { get; } = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The MQL5 <c>datetime</c> value for <paramref name="value"/>. Values at or
    /// before the epoch clamp to 0, which is what MQL5 stores for an unset datetime.
    /// </summary>
    public static long FromDateTime(DateTime value)
    {
        DateTime utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        long seconds = (long)(utc - Epoch).TotalSeconds;
        return seconds < 0 ? 0 : seconds;
    }

    /// <summary>The UTC <see cref="DateTime"/> for an MQL5 <c>datetime</c> value.</summary>
    public static DateTime ToDateTime(long datetime)
    {
        if (datetime <= 0)
        {
            return Epoch;
        }

        // 253402300799 is 9999-12-31T23:59:59Z, the largest DateTime the CLR holds.
        long clamped = datetime > 253402300799L ? 253402300799L : datetime;
        return Epoch.AddSeconds(clamped);
    }

    /// <summary>Fills <paramref name="target"/> from an MQL5 <c>datetime</c> value.</summary>
    public static void ToStruct(long datetime, out Mql5DateTime target)
    {
        DateTime moment = ToDateTime(datetime);
        target = new Mql5DateTime
        {
            Year = moment.Year,
            Month = moment.Month,
            Day = moment.Day,
            Hour = moment.Hour,
            Minute = moment.Minute,
            Second = moment.Second,
            DayOfWeek = (int)moment.DayOfWeek,
            DayOfYear = moment.DayOfYear
        };
    }

    /// <summary>
    /// The MQL5 <c>datetime</c> value for a broken-down structure. Out-of-range
    /// fields yield 0 rather than throwing: <c>StructToTime</c> is a supported
    /// built-in and supported built-ins return MQL5 failure values instead of
    /// raising.
    /// </summary>
    public static long FromStruct(in Mql5DateTime value)
    {
        try
        {
            DateTime moment = new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
            return FromDateTime(moment);
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}
