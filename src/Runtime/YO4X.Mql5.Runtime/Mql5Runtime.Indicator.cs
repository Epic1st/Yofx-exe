using System.Globalization;
using System.Text;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 technical-indicator functions. Every <c>iXxx</c> entry is
/// <b>IndicatorBound</b>: it resolves a handle through
/// <see cref="IMql5MarketContext.IndicatorHandle"/> and the values are read back later
/// through <c>CopyBuffer</c>, exactly as MQL5 works.
///
/// <b>No indicator mathematics lives in this library.</b> That is not an omission - a
/// moving average computed here would disagree in the last decimal with the one the
/// engine computes for the same bars, and a strategy comparing an <c>iMA</c> value
/// against a price would then take a different branch than it does in the engine. One
/// implementation, on the engine side, is the only arrangement that cannot drift.
///
/// Handles are cached by name and argument list, because MQL5 returns the same handle
/// for identical parameters and a strategy that calls <c>iMA</c> on every tick would
/// otherwise ask the engine to build a new indicator on every tick.
///
/// <c>iCustom</c> is refused: it loads a third-party compiled indicator, and there is
/// nothing in the engine that can stand in for code we do not have.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>iAC</c>, Accelerator Oscillator. IndicatorBound.</summary>
    int IAC(string? symbol, int period);

    /// <summary>MQL5 <c>iAD</c>, Accumulation/Distribution. IndicatorBound.</summary>
    int IAD(string? symbol, int period, int appliedVolume);

    /// <summary>MQL5 <c>iADX</c>. IndicatorBound.</summary>
    int IADX(string? symbol, int period, int adxPeriod);

    /// <summary>MQL5 <c>iADXWilder</c>. IndicatorBound.</summary>
    int IADXWilder(string? symbol, int period, int adxPeriod);

    /// <summary>MQL5 <c>iAlligator</c>. IndicatorBound.</summary>
    int IAlligator(string? symbol, int period, int jawPeriod, int jawShift, int teethPeriod, int teethShift, int lipsPeriod, int lipsShift, int maMethod, int appliedPrice);

    /// <summary>MQL5 <c>iAMA</c>, Adaptive Moving Average. IndicatorBound.</summary>
    int IAMA(string? symbol, int period, int amaPeriod, int fastMaPeriod, int slowMaPeriod, int amaShift, int appliedPrice);

    /// <summary>MQL5 <c>iAO</c>, Awesome Oscillator. IndicatorBound.</summary>
    int IAO(string? symbol, int period);

    /// <summary>MQL5 <c>iATR</c>. IndicatorBound.</summary>
    int IATR(string? symbol, int period, int maPeriod);

    /// <summary>MQL5 <c>iBands</c>, Bollinger Bands. IndicatorBound.</summary>
    int IBands(string? symbol, int period, int bandsPeriod, int bandsShift, double deviation, int appliedPrice);

    /// <summary>MQL5 <c>iBearsPower</c>. IndicatorBound.</summary>
    int IBearsPower(string? symbol, int period, int maPeriod);

    /// <summary>MQL5 <c>iBullsPower</c>. IndicatorBound.</summary>
    int IBullsPower(string? symbol, int period, int maPeriod);

    /// <summary>MQL5 <c>iBWMFI</c>, Market Facilitation Index. IndicatorBound.</summary>
    int IBWMFI(string? symbol, int period, int appliedVolume);

    /// <summary>MQL5 <c>iCCI</c>. IndicatorBound.</summary>
    int ICCI(string? symbol, int period, int maPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iChaikin</c>. IndicatorBound.</summary>
    int IChaikin(string? symbol, int period, int fastMaPeriod, int slowMaPeriod, int maMethod, int appliedVolume);

    /// <summary>MQL5 <c>iDEMA</c>. IndicatorBound.</summary>
    int IDEMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice);

    /// <summary>MQL5 <c>iDeMarker</c>. IndicatorBound.</summary>
    int IDeMarker(string? symbol, int period, int maPeriod);

    /// <summary>MQL5 <c>iEnvelopes</c>. IndicatorBound.</summary>
    int IEnvelopes(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice, double deviation);

    /// <summary>MQL5 <c>iForce</c>. IndicatorBound.</summary>
    int IForce(string? symbol, int period, int maPeriod, int maMethod, int appliedVolume);

    /// <summary>MQL5 <c>iFractals</c>. IndicatorBound.</summary>
    int IFractals(string? symbol, int period);

    /// <summary>MQL5 <c>iFrAMA</c>. IndicatorBound.</summary>
    int IFrAMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice);

    /// <summary>MQL5 <c>iGator</c>. IndicatorBound.</summary>
    int IGator(string? symbol, int period, int jawPeriod, int jawShift, int teethPeriod, int teethShift, int lipsPeriod, int lipsShift, int maMethod, int appliedPrice);

    /// <summary>MQL5 <c>iIchimoku</c>. IndicatorBound.</summary>
    int IIchimoku(string? symbol, int period, int tenkanSen, int kijunSen, int senkouSpanB);

    /// <summary>MQL5 <c>iMA</c>. IndicatorBound.</summary>
    int IMA(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice);

    /// <summary>MQL5 <c>iMACD</c>. IndicatorBound.</summary>
    int IMACD(string? symbol, int period, int fastEmaPeriod, int slowEmaPeriod, int signalPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iMFI</c>. IndicatorBound.</summary>
    int IMFI(string? symbol, int period, int maPeriod, int appliedVolume);

    /// <summary>MQL5 <c>iMomentum</c>. IndicatorBound.</summary>
    int IMomentum(string? symbol, int period, int momPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iOBV</c>. IndicatorBound.</summary>
    int IOBV(string? symbol, int period, int appliedVolume);

    /// <summary>MQL5 <c>iOsMA</c>. IndicatorBound.</summary>
    int IOsMA(string? symbol, int period, int fastEmaPeriod, int slowEmaPeriod, int signalPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iRSI</c>. IndicatorBound.</summary>
    int IRSI(string? symbol, int period, int maPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iRVI</c>. IndicatorBound.</summary>
    int IRVI(string? symbol, int period, int maPeriod);

    /// <summary>MQL5 <c>iSAR</c>, Parabolic SAR. IndicatorBound.</summary>
    int ISAR(string? symbol, int period, double stepValue, double maximum);

    /// <summary>MQL5 <c>iStdDev</c>. IndicatorBound.</summary>
    int IStdDev(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice);

    /// <summary>MQL5 <c>iStochastic</c>. IndicatorBound.</summary>
    int IStochastic(string? symbol, int period, int kPeriod, int dPeriod, int slowing, int maMethod, int priceField);

    /// <summary>MQL5 <c>iTEMA</c>. IndicatorBound.</summary>
    int ITEMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice);

    /// <summary>MQL5 <c>iTriX</c>. IndicatorBound.</summary>
    int ITriX(string? symbol, int period, int maPeriod, int appliedPrice);

    /// <summary>MQL5 <c>iVIDyA</c>. IndicatorBound.</summary>
    int IVIDyA(string? symbol, int period, int cmoPeriod, int emaPeriod, int maShift, int appliedPrice);

    /// <summary>MQL5 <c>iVolumes</c>. IndicatorBound.</summary>
    int IVolumes(string? symbol, int period, int appliedVolume);

    /// <summary>MQL5 <c>iWPR</c>, Williams Percent Range. IndicatorBound.</summary>
    int IWPR(string? symbol, int period, int calcPeriod);

    /// <summary>
    /// MQL5 <c>iCustom</c>. Unsupported: it loads a third-party compiled indicator, and
    /// nothing in the engine can stand in for code that was never converted.
    /// </summary>
    int ICustom(string? symbol, int period, string? name, params object?[]? parameters);

    /// <summary>MQL5 <c>IndicatorCreate</c>. IndicatorBound.</summary>
    int IndicatorCreate(string? symbol, int period, int indicatorType, Mql5Param[]? parameters = null);

    /// <summary>MQL5 <c>IndicatorRelease</c>. IndicatorBound.</summary>
    bool IndicatorRelease(int indicatorHandle);

    /// <summary>MQL5 <c>BarsCalculated</c>. Returns -1 when the handle has no data yet. IndicatorBound.</summary>
    int BarsCalculated(int indicatorHandle);

    /// <summary>MQL5 <c>CopyBuffer</c>, start-position form. IndicatorBound.</summary>
    int CopyBuffer(int indicatorHandle, int bufferNumber, int startPosition, int count, ref double[]? buffer);

    /// <summary>MQL5 <c>CopyBuffer</c>, start-time form. IndicatorBound.</summary>
    int CopyBuffer(int indicatorHandle, int bufferNumber, long startTime, int count, ref double[]? buffer);

    /// <summary>MQL5 <c>CopyBuffer</c>, time-range form. IndicatorBound.</summary>
    int CopyBuffer(int indicatorHandle, int bufferNumber, long startTime, long stopTime, ref double[]? buffer);

    /// <summary>MQL5 <c>SetIndexBuffer</c>. Only meaningful inside a converted custom indicator. EngineBound.</summary>
    bool SetIndexBuffer(int index, double[]? buffer, int dataType = 0);

    /// <summary>MQL5 <c>IndicatorSetDouble</c>. Visual only. ChartStub.</summary>
    bool IndicatorSetDouble(int propertyId, double value);

    /// <summary>MQL5 <c>IndicatorSetDouble</c> with a modifier. Visual only. ChartStub.</summary>
    bool IndicatorSetDouble(int propertyId, int propertyModifier, double value);

    /// <summary>MQL5 <c>IndicatorSetInteger</c>. Visual only. ChartStub.</summary>
    bool IndicatorSetInteger(int propertyId, int value);

    /// <summary>MQL5 <c>IndicatorSetInteger</c> with a modifier. Visual only. ChartStub.</summary>
    bool IndicatorSetInteger(int propertyId, int propertyModifier, int value);

    /// <summary>MQL5 <c>IndicatorSetString</c>. Visual only. ChartStub.</summary>
    bool IndicatorSetString(int propertyId, string? value);

    /// <summary>MQL5 <c>IndicatorSetString</c> with a modifier. Visual only. ChartStub.</summary>
    bool IndicatorSetString(int propertyId, int propertyModifier, string? value);

    /// <summary>MQL5 <c>PlotIndexGetInteger</c>. Visual only. ChartStub.</summary>
    int PlotIndexGetInteger(int plotIndex, int propertyId);

    /// <summary>MQL5 <c>PlotIndexGetInteger</c> with a modifier. Visual only. ChartStub.</summary>
    int PlotIndexGetInteger(int plotIndex, int propertyId, int propertyModifier);

    /// <summary>MQL5 <c>PlotIndexSetDouble</c>. Visual only. ChartStub.</summary>
    bool PlotIndexSetDouble(int plotIndex, int propertyId, double value);

    /// <summary>MQL5 <c>PlotIndexSetInteger</c>. Visual only. ChartStub.</summary>
    bool PlotIndexSetInteger(int plotIndex, int propertyId, int value);

    /// <summary>MQL5 <c>PlotIndexSetInteger</c> with a modifier. Visual only. ChartStub.</summary>
    bool PlotIndexSetInteger(int plotIndex, int propertyId, int propertyModifier, int value);

    /// <summary>MQL5 <c>PlotIndexSetString</c>. Visual only. ChartStub.</summary>
    bool PlotIndexSetString(int plotIndex, int propertyId, string? value);
}

public sealed partial class Mql5Runtime
{
    // A unit separator: it cannot occur in a symbol name, so two different argument
    // lists can never collide into one cache key.
    private const char ArgumentSeparator = (char)0x1F;

    private readonly Dictionary<(int PlotIndex, int PropertyId, int Modifier), long> plotIntegers = [];

    /// <inheritdoc />
    public int IAC(string? symbol, int period) => Handle("iAC", symbol, period);

    /// <inheritdoc />
    public int IAD(string? symbol, int period, int appliedVolume) => Handle("iAD", symbol, period, appliedVolume);

    /// <inheritdoc />
    public int IADX(string? symbol, int period, int adxPeriod) => Handle("iADX", symbol, period, adxPeriod);

    /// <inheritdoc />
    public int IADXWilder(string? symbol, int period, int adxPeriod) => Handle("iADXWilder", symbol, period, adxPeriod);

    /// <inheritdoc />
    public int IAlligator(string? symbol, int period, int jawPeriod, int jawShift, int teethPeriod, int teethShift, int lipsPeriod, int lipsShift, int maMethod, int appliedPrice)
        => Handle("iAlligator", symbol, period, jawPeriod, jawShift, teethPeriod, teethShift, lipsPeriod, lipsShift, maMethod, appliedPrice);

    /// <inheritdoc />
    public int IAMA(string? symbol, int period, int amaPeriod, int fastMaPeriod, int slowMaPeriod, int amaShift, int appliedPrice)
        => Handle("iAMA", symbol, period, amaPeriod, fastMaPeriod, slowMaPeriod, amaShift, appliedPrice);

    /// <inheritdoc />
    public int IAO(string? symbol, int period) => Handle("iAO", symbol, period);

    /// <inheritdoc />
    public int IATR(string? symbol, int period, int maPeriod) => Handle("iATR", symbol, period, maPeriod);

    /// <inheritdoc />
    public int IBands(string? symbol, int period, int bandsPeriod, int bandsShift, double deviation, int appliedPrice)
        => Handle("iBands", symbol, period, bandsPeriod, bandsShift, deviation, appliedPrice);

    /// <inheritdoc />
    public int IBearsPower(string? symbol, int period, int maPeriod) => Handle("iBearsPower", symbol, period, maPeriod);

    /// <inheritdoc />
    public int IBullsPower(string? symbol, int period, int maPeriod) => Handle("iBullsPower", symbol, period, maPeriod);

    /// <inheritdoc />
    public int IBWMFI(string? symbol, int period, int appliedVolume) => Handle("iBWMFI", symbol, period, appliedVolume);

    /// <inheritdoc />
    public int ICCI(string? symbol, int period, int maPeriod, int appliedPrice) => Handle("iCCI", symbol, period, maPeriod, appliedPrice);

    /// <inheritdoc />
    public int IChaikin(string? symbol, int period, int fastMaPeriod, int slowMaPeriod, int maMethod, int appliedVolume)
        => Handle("iChaikin", symbol, period, fastMaPeriod, slowMaPeriod, maMethod, appliedVolume);

    /// <inheritdoc />
    public int IDEMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice)
        => Handle("iDEMA", symbol, period, maPeriod, maShift, appliedPrice);

    /// <inheritdoc />
    public int IDeMarker(string? symbol, int period, int maPeriod) => Handle("iDeMarker", symbol, period, maPeriod);

    /// <inheritdoc />
    public int IEnvelopes(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice, double deviation)
        => Handle("iEnvelopes", symbol, period, maPeriod, maShift, maMethod, appliedPrice, deviation);

    /// <inheritdoc />
    public int IForce(string? symbol, int period, int maPeriod, int maMethod, int appliedVolume)
        => Handle("iForce", symbol, period, maPeriod, maMethod, appliedVolume);

    /// <inheritdoc />
    public int IFractals(string? symbol, int period) => Handle("iFractals", symbol, period);

    /// <inheritdoc />
    public int IFrAMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice)
        => Handle("iFrAMA", symbol, period, maPeriod, maShift, appliedPrice);

    /// <inheritdoc />
    public int IGator(string? symbol, int period, int jawPeriod, int jawShift, int teethPeriod, int teethShift, int lipsPeriod, int lipsShift, int maMethod, int appliedPrice)
        => Handle("iGator", symbol, period, jawPeriod, jawShift, teethPeriod, teethShift, lipsPeriod, lipsShift, maMethod, appliedPrice);

    /// <inheritdoc />
    public int IIchimoku(string? symbol, int period, int tenkanSen, int kijunSen, int senkouSpanB)
        => Handle("iIchimoku", symbol, period, tenkanSen, kijunSen, senkouSpanB);

    /// <inheritdoc />
    public int IMA(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice)
        => Handle("iMA", symbol, period, maPeriod, maShift, maMethod, appliedPrice);

    /// <inheritdoc />
    public int IMACD(string? symbol, int period, int fastEmaPeriod, int slowEmaPeriod, int signalPeriod, int appliedPrice)
        => Handle("iMACD", symbol, period, fastEmaPeriod, slowEmaPeriod, signalPeriod, appliedPrice);

    /// <inheritdoc />
    public int IMFI(string? symbol, int period, int maPeriod, int appliedVolume) => Handle("iMFI", symbol, period, maPeriod, appliedVolume);

    /// <inheritdoc />
    public int IMomentum(string? symbol, int period, int momPeriod, int appliedPrice) => Handle("iMomentum", symbol, period, momPeriod, appliedPrice);

    /// <inheritdoc />
    public int IOBV(string? symbol, int period, int appliedVolume) => Handle("iOBV", symbol, period, appliedVolume);

    /// <inheritdoc />
    public int IOsMA(string? symbol, int period, int fastEmaPeriod, int slowEmaPeriod, int signalPeriod, int appliedPrice)
        => Handle("iOsMA", symbol, period, fastEmaPeriod, slowEmaPeriod, signalPeriod, appliedPrice);

    /// <inheritdoc />
    public int IRSI(string? symbol, int period, int maPeriod, int appliedPrice) => Handle("iRSI", symbol, period, maPeriod, appliedPrice);

    /// <inheritdoc />
    public int IRVI(string? symbol, int period, int maPeriod) => Handle("iRVI", symbol, period, maPeriod);

    /// <inheritdoc />
    public int ISAR(string? symbol, int period, double stepValue, double maximum) => Handle("iSAR", symbol, period, stepValue, maximum);

    /// <inheritdoc />
    public int IStdDev(string? symbol, int period, int maPeriod, int maShift, int maMethod, int appliedPrice)
        => Handle("iStdDev", symbol, period, maPeriod, maShift, maMethod, appliedPrice);

    /// <inheritdoc />
    public int IStochastic(string? symbol, int period, int kPeriod, int dPeriod, int slowing, int maMethod, int priceField)
        => Handle("iStochastic", symbol, period, kPeriod, dPeriod, slowing, maMethod, priceField);

    /// <inheritdoc />
    public int ITEMA(string? symbol, int period, int maPeriod, int maShift, int appliedPrice)
        => Handle("iTEMA", symbol, period, maPeriod, maShift, appliedPrice);

    /// <inheritdoc />
    public int ITriX(string? symbol, int period, int maPeriod, int appliedPrice) => Handle("iTriX", symbol, period, maPeriod, appliedPrice);

    /// <inheritdoc />
    public int IVIDyA(string? symbol, int period, int cmoPeriod, int emaPeriod, int maShift, int appliedPrice)
        => Handle("iVIDyA", symbol, period, cmoPeriod, emaPeriod, maShift, appliedPrice);

    /// <inheritdoc />
    public int IVolumes(string? symbol, int period, int appliedVolume) => Handle("iVolumes", symbol, period, appliedVolume);

    /// <inheritdoc />
    public int IWPR(string? symbol, int period, int calcPeriod) => Handle("iWPR", symbol, period, calcPeriod);

    /// <inheritdoc />
    public int ICustom(string? symbol, int period, string? name, params object?[]? parameters)
        => throw Refuse(nameof(ICustom), "it loads a third-party compiled indicator that was never converted");

    /// <inheritdoc />
    public int IndicatorCreate(string? symbol, int period, int indicatorType, Mql5Param[]? parameters = null)
    {
        object[] arguments = new object[2 + (parameters?.Length ?? 0)];
        arguments[0] = Timeframe(period);
        arguments[1] = indicatorType;
        for (int index = 0; index < (parameters?.Length ?? 0); index++)
        {
            arguments[2 + index] = parameters![index];
        }

        int handle = context.IndicatorHandle("IndicatorCreate", arguments);
        if (handle == Mql5Constants.InvalidHandle)
        {
            SetError(Mql5ErrorCodes.IndicatorCannotCreate);
        }

        return handle;
    }

    /// <inheritdoc />
    public bool IndicatorRelease(int indicatorHandle) => context.IndicatorRelease(indicatorHandle);

    /// <inheritdoc />
    public int BarsCalculated(int indicatorHandle) => context.BarsCalculated(indicatorHandle);

    /// <inheritdoc />
    public int CopyBuffer(int indicatorHandle, int bufferNumber, int startPosition, int count, ref double[]? buffer)
        => CopyBufferCore(indicatorHandle, bufferNumber, Mql5CopyRange.FromPosition(startPosition, count), ref buffer);

    /// <inheritdoc />
    public int CopyBuffer(int indicatorHandle, int bufferNumber, long startTime, int count, ref double[]? buffer)
        => CopyBufferCore(indicatorHandle, bufferNumber, Mql5CopyRange.FromTime(startTime, count), ref buffer);

    /// <inheritdoc />
    public int CopyBuffer(int indicatorHandle, int bufferNumber, long startTime, long stopTime, ref double[]? buffer)
        => CopyBufferCore(indicatorHandle, bufferNumber, Mql5CopyRange.TimeRange(startTime, stopTime), ref buffer);

    /// <inheritdoc />
    public bool SetIndexBuffer(int index, double[]? buffer, int dataType = 0)
        => context.SetIndexBuffer(index, buffer ?? [], dataType);

    /// <inheritdoc />
    public bool IndicatorSetDouble(int propertyId, double value) => RecordPlot(nameof(IndicatorSetDouble));

    /// <inheritdoc />
    public bool IndicatorSetDouble(int propertyId, int propertyModifier, double value) => RecordPlot(nameof(IndicatorSetDouble));

    /// <inheritdoc />
    public bool IndicatorSetInteger(int propertyId, int value) => RecordPlot(nameof(IndicatorSetInteger));

    /// <inheritdoc />
    public bool IndicatorSetInteger(int propertyId, int propertyModifier, int value) => RecordPlot(nameof(IndicatorSetInteger));

    /// <inheritdoc />
    public bool IndicatorSetString(int propertyId, string? value) => RecordPlot(nameof(IndicatorSetString));

    /// <inheritdoc />
    public bool IndicatorSetString(int propertyId, int propertyModifier, string? value) => RecordPlot(nameof(IndicatorSetString));

    /// <inheritdoc />
    public int PlotIndexGetInteger(int plotIndex, int propertyId) => PlotIndexGetInteger(plotIndex, propertyId, 0);

    /// <inheritdoc />
    public int PlotIndexGetInteger(int plotIndex, int propertyId, int propertyModifier)
    {
        RecordPlot(nameof(PlotIndexGetInteger));
        return plotIntegers.TryGetValue((plotIndex, propertyId, propertyModifier), out long value) ? (int)value : 0;
    }

    /// <inheritdoc />
    public bool PlotIndexSetDouble(int plotIndex, int propertyId, double value) => RecordPlot(nameof(PlotIndexSetDouble));

    /// <inheritdoc />
    public bool PlotIndexSetInteger(int plotIndex, int propertyId, int value) => PlotIndexSetInteger(plotIndex, propertyId, 0, value);

    /// <inheritdoc />
    public bool PlotIndexSetInteger(int plotIndex, int propertyId, int propertyModifier, int value)
    {
        plotIntegers[(plotIndex, propertyId, propertyModifier)] = value;
        return RecordPlot(nameof(PlotIndexSetInteger));
    }

    /// <inheritdoc />
    public bool PlotIndexSetString(int plotIndex, int propertyId, string? value) => RecordPlot(nameof(PlotIndexSetString));

    private bool RecordPlot(string function)
    {
        ChartObjects.Record();
        RecordChartCall(function);
        return true;
    }

    private int CopyBufferCore(int indicatorHandle, int bufferNumber, Mql5CopyRange range, ref double[]? buffer)
    {
        if (indicatorHandle == Mql5Constants.InvalidHandle)
        {
            SetError(Mql5ErrorCodes.IndicatorCannotCreate);
            return -1;
        }

        double[] target = buffer ?? [];
        if (range.Kind != Mql5CopyRangeKind.TimeRange && range.Count > 0 && target.Length < range.Count)
        {
            Array.Resize(ref target, range.Count);
        }

        int written = context.CopyBufferRange(indicatorHandle, bufferNumber, range, ref target);
        buffer = target;
        return Finish(written, buffer);
    }

    private int Handle(string name, string? symbol, int period, params object[] parameters)
    {
        string resolvedSymbol = Resolve(symbol);
        int resolvedPeriod = Timeframe(period);

        object[] arguments = new object[2 + parameters.Length];
        arguments[0] = resolvedSymbol;
        arguments[1] = resolvedPeriod;
        Array.Copy(parameters, 0, arguments, 2, parameters.Length);

        string key = CacheKey(name, arguments);
        if (indicatorHandles.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int handle = context.IndicatorHandle(name, arguments);
        if (handle == Mql5Constants.InvalidHandle)
        {
            SetError(Mql5ErrorCodes.IndicatorCannotCreate);
            return handle;
        }

        indicatorHandles[key] = handle;
        return handle;
    }

    private static string CacheKey(string name, object[] arguments)
    {
        StringBuilder builder = new(name.Length + (arguments.Length * 8));
        builder.Append(name);
        foreach (object argument in arguments)
        {
            builder.Append(ArgumentSeparator);
            builder.Append(argument switch
            {
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => argument.ToString()
            });
        }

        return builder.ToString();
    }
}
