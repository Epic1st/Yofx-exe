namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 date and time functions.
///
/// The split here is between calendar arithmetic and asking what time it is.
/// <c>TimeToStruct</c> and <c>StructToTime</c> are <b>Native</b>: they only take a
/// number apart and put it back together. Everything that answers "now" is
/// <b>EngineBound</b>, because in a backtest "now" is the simulated bar time, not the
/// wall clock. Reading <see cref="DateTime.UtcNow"/> here would make every
/// time-of-day filter in the corpus behave differently on replay than it did on the
/// original run.
///
/// MQL5 <c>datetime</c> is seconds since 1970-01-01, and it is an integer type: the
/// corpus adds, subtracts and compares datetimes freely. This surface therefore
/// speaks <see cref="long"/>, and <see cref="Mql5Time"/> converts at the
/// <see cref="IMql5MarketContext"/> boundary.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>TimeCurrent</c>: the last known trade server time. EngineBound.</summary>
    long TimeCurrent();

    /// <summary>MQL5 <c>TimeCurrent</c> with the broken-down form filled in. EngineBound.</summary>
    long TimeCurrent(out Mql5DateTime moment);

    /// <summary>MQL5 <c>TimeLocal</c>: the computer clock, as the engine reports it. EngineBound.</summary>
    long TimeLocal();

    /// <summary>MQL5 <c>TimeLocal</c> with the broken-down form filled in. EngineBound.</summary>
    long TimeLocal(out Mql5DateTime moment);

    /// <summary>MQL5 <c>TimeGMT</c>. EngineBound.</summary>
    long TimeGmt();

    /// <summary>MQL5 <c>TimeGMT</c> with the broken-down form filled in. EngineBound.</summary>
    long TimeGmt(out Mql5DateTime moment);

    /// <summary>MQL5 <c>TimeTradeServer</c>. EngineBound.</summary>
    long TimeTradeServer();

    /// <summary>MQL5 <c>TimeTradeServer</c> with the broken-down form filled in. EngineBound.</summary>
    long TimeTradeServer(out Mql5DateTime moment);

    /// <summary>MQL5 <c>TimeGMTOffset</c>, in seconds. EngineBound.</summary>
    int TimeGmtOffset();

    /// <summary>MQL5 <c>TimeDaylightSavings</c>, in seconds. EngineBound.</summary>
    int TimeDaylightSavings();

    /// <summary>
    /// MQL5 <c>PeriodSeconds</c>. <c>PERIOD_CURRENT</c> resolves through the engine's
    /// current timeframe, which is why this is EngineBound rather than a lookup table.
    /// </summary>
    int PeriodSeconds(int period = Mql5Constants.Timeframes.Current);

    /// <summary>MQL5 <c>TimeToStruct</c>. Native.</summary>
    bool TimeToStruct(long value, out Mql5DateTime moment);

    /// <summary>MQL5 <c>StructToTime</c>. Returns 0 for a structure that is not a real date. Native.</summary>
    long StructToTime(in Mql5DateTime moment);
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public long TimeCurrent() => Mql5Time.FromDateTime(context.TimeCurrent);

    /// <inheritdoc />
    public long TimeCurrent(out Mql5DateTime moment)
    {
        long value = TimeCurrent();
        Mql5Time.ToStruct(value, out moment);
        return value;
    }

    /// <inheritdoc />
    public long TimeLocal() => Mql5Time.FromDateTime(context.TimeLocal);

    /// <inheritdoc />
    public long TimeLocal(out Mql5DateTime moment)
    {
        long value = TimeLocal();
        Mql5Time.ToStruct(value, out moment);
        return value;
    }

    /// <inheritdoc />
    public long TimeGmt() => Mql5Time.FromDateTime(context.TimeGmt);

    /// <inheritdoc />
    public long TimeGmt(out Mql5DateTime moment)
    {
        long value = TimeGmt();
        Mql5Time.ToStruct(value, out moment);
        return value;
    }

    /// <inheritdoc />
    public long TimeTradeServer() => Mql5Time.FromDateTime(context.TimeTradeServer);

    /// <inheritdoc />
    public long TimeTradeServer(out Mql5DateTime moment)
    {
        long value = TimeTradeServer();
        Mql5Time.ToStruct(value, out moment);
        return value;
    }

    /// <inheritdoc />
    public int TimeGmtOffset() => context.TimeGmtOffset;

    /// <inheritdoc />
    public int TimeDaylightSavings() => context.TimeDaylightSavings;

    /// <inheritdoc />
    public int PeriodSeconds(int period = Mql5Constants.Timeframes.Current)
    {
        int effective = period == Mql5Constants.Timeframes.Current ? context.Period : period;
        return Mql5Constants.Timeframes.Seconds(effective);
    }

    /// <inheritdoc />
    public bool TimeToStruct(long value, out Mql5DateTime moment)
    {
        Mql5Time.ToStruct(value, out moment);
        return true;
    }

    /// <inheritdoc />
    public long StructToTime(in Mql5DateTime moment)
    {
        long value = Mql5Time.FromStruct(moment);
        if (value == 0)
        {
            SetError(Mql5ErrorCodes.InvalidDatetime);
        }

        return value;
    }
}
