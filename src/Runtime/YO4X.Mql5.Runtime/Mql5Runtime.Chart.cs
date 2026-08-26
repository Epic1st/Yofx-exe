namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5 graphical-object and chart-property functions. Every one is a
/// <b>ChartStub</b>: visual only, recorded and answered from the recording, never
/// drawn. Two of them - <c>ChartSaveTemplate</c> and <c>ChartScreenShot</c> - are
/// <b>Unsupported</b> instead, because they write files.
///
/// This is by a wide margin the largest block of the corpus: <c>ObjectSetInteger</c>
/// alone has more callsites than any other built-in, and <c>ObjectCreate</c>,
/// <c>ObjectSetString</c> and <c>ObjectDelete</c> are not far behind. None of it moves
/// an order or changes an indicator value, so a backtest loses nothing by not drawing.
///
/// What it would lose by forgetting is real, which is why these are recording stubs
/// rather than blind no-ops. MQL5 dashboard code routinely stores state on its own
/// objects - a button's pressed flag, a label's text, a counter in a property - and
/// reads it back on the next tick. It also counts objects, walks them by index and
/// deletes them by name prefix. A stub that answered 0 and false to all of that would
/// send the strategy down branches it would never take on a terminal.
///
/// The one thing not recorded is geometry. <c>ObjectGetValueByTime</c> and
/// <c>ObjectGetTimeByValue</c> interpolate along a drawn trendline, which needs a
/// chart scale that does not exist here; they answer 0.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>MQL5 <c>ObjectCreate</c>. Recorded; returns false if the name is taken, as MQL5 does. ChartStub.</summary>
    bool ObjectCreate(long chartId, string? name, int type, int subWindow, long time1, double price1, long time2 = 0, double price2 = 0, long time3 = 0, double price3 = 0);

    /// <summary>MQL5 <c>ObjectDelete</c>. ChartStub.</summary>
    bool ObjectDelete(long chartId, string? name);

    /// <summary>MQL5 <c>ObjectFind</c>. Returns the sub-window, or -1 when the object is unknown. ChartStub.</summary>
    int ObjectFind(long chartId, string? name);

    /// <summary>MQL5 <c>ObjectMove</c>. ChartStub.</summary>
    bool ObjectMove(long chartId, string? name, int pointIndex, long time, double price);

    /// <summary>MQL5 <c>ObjectName</c>. ChartStub.</summary>
    string ObjectName(long chartId, int position, int subWindow = -1, int type = -1);

    /// <summary>MQL5 <c>ObjectsTotal</c>. ChartStub.</summary>
    int ObjectsTotal(long chartId, int subWindow = -1, int type = -1);

    /// <summary>MQL5 <c>ObjectsDeleteAll</c>, the sub-window and type form. ChartStub.</summary>
    int ObjectsDeleteAll(long chartId, int subWindow = -1, int type = -1);

    /// <summary>MQL5 <c>ObjectsDeleteAll</c>, the name-prefix form. ChartStub.</summary>
    int ObjectsDeleteAll(long chartId, string? prefix, int subWindow = -1, int objectType = -1);

    /// <summary>MQL5 <c>ObjectSetInteger</c>. ChartStub.</summary>
    bool ObjectSetInteger(long chartId, string? name, int propertyId, long value);

    /// <summary>MQL5 <c>ObjectSetInteger</c> with a modifier. ChartStub.</summary>
    bool ObjectSetInteger(long chartId, string? name, int propertyId, int propertyModifier, long value);

    /// <summary>MQL5 <c>ObjectGetInteger</c>, direct-return form. ChartStub.</summary>
    long ObjectGetInteger(long chartId, string? name, int propertyId, int propertyModifier = 0);

    /// <summary>MQL5 <c>ObjectGetInteger</c>, out-parameter form. ChartStub.</summary>
    bool ObjectGetInteger(long chartId, string? name, int propertyId, int propertyModifier, out long value);

    /// <summary>MQL5 <c>ObjectSetDouble</c>. ChartStub.</summary>
    bool ObjectSetDouble(long chartId, string? name, int propertyId, double value);

    /// <summary>MQL5 <c>ObjectSetDouble</c> with a modifier. ChartStub.</summary>
    bool ObjectSetDouble(long chartId, string? name, int propertyId, int propertyModifier, double value);

    /// <summary>MQL5 <c>ObjectGetDouble</c>, direct-return form. ChartStub.</summary>
    double ObjectGetDouble(long chartId, string? name, int propertyId, int propertyModifier = 0);

    /// <summary>MQL5 <c>ObjectGetDouble</c>, out-parameter form. ChartStub.</summary>
    bool ObjectGetDouble(long chartId, string? name, int propertyId, int propertyModifier, out double value);

    /// <summary>MQL5 <c>ObjectSetString</c>. ChartStub.</summary>
    bool ObjectSetString(long chartId, string? name, int propertyId, string? value);

    /// <summary>MQL5 <c>ObjectSetString</c> with a modifier. ChartStub.</summary>
    bool ObjectSetString(long chartId, string? name, int propertyId, int propertyModifier, string? value);

    /// <summary>MQL5 <c>ObjectGetString</c>, direct-return form. ChartStub.</summary>
    string ObjectGetString(long chartId, string? name, int propertyId, int propertyModifier = 0);

    /// <summary>MQL5 <c>ObjectGetString</c>, out-parameter form. ChartStub.</summary>
    bool ObjectGetString(long chartId, string? name, int propertyId, int propertyModifier, out string value);

    /// <summary>
    /// MQL5 <c>ObjectGetValueByTime</c>. Answers 0: interpolating along a drawn line
    /// needs a chart scale that does not exist in a backtest. ChartStub.
    /// </summary>
    double ObjectGetValueByTime(long chartId, string? name, long time, int lineId = 0);

    /// <summary>MQL5 <c>ObjectGetTimeByValue</c>. Answers 0, for the same reason as <see cref="ObjectGetValueByTime"/>. ChartStub.</summary>
    long ObjectGetTimeByValue(long chartId, string? name, double value, int lineId = 0);

    /// <summary>
    /// MQL5 <c>TextGetSize</c>. Reports a nominal metric rather than failing, because
    /// panel code lays itself out from the answer and a false return collapses the
    /// layout. ChartStub.
    /// </summary>
    bool TextGetSize(string? text, out uint width, out uint height);

    /// <summary>MQL5 <c>TextOut</c>. ChartStub.</summary>
    bool TextOut(string? text, int x, int y, uint anchor, uint[]? data, uint width, uint height, uint color, int colorFormat);

    /// <summary>MQL5 <c>TextSetFont</c>. ChartStub.</summary>
    bool TextSetFont(string? name, int size, uint flags = 0, int orientation = 0);

    /// <summary>MQL5 <c>ChartID</c>. ChartStub.</summary>
    long ChartId();

    /// <summary>MQL5 <c>ChartFirst</c>. ChartStub.</summary>
    long ChartFirst();

    /// <summary>MQL5 <c>ChartNext</c>. Returns -1: this runtime holds exactly one chart. ChartStub.</summary>
    long ChartNext(long chartId);

    /// <summary>MQL5 <c>ChartOpen</c>. ChartStub.</summary>
    long ChartOpen(string? symbol, int period);

    /// <summary>MQL5 <c>ChartClose</c>. ChartStub.</summary>
    bool ChartClose(long chartId = 0);

    /// <summary>MQL5 <c>ChartRedraw</c>. ChartStub.</summary>
    void ChartRedraw(long chartId = 0);

    /// <summary>MQL5 <c>ChartSymbol</c>. ChartStub, answered from the market context. </summary>
    string ChartSymbol(long chartId = 0);

    /// <summary>MQL5 <c>ChartPeriod</c>. ChartStub, answered from the market context.</summary>
    int ChartPeriod(long chartId = 0);

    /// <summary>MQL5 <c>ChartSetSymbolPeriod</c>. ChartStub.</summary>
    bool ChartSetSymbolPeriod(long chartId, string? symbol, int period);

    /// <summary>MQL5 <c>ChartApplyTemplate</c>. ChartStub.</summary>
    bool ChartApplyTemplate(long chartId, string? filename);

    /// <summary>MQL5 <c>ChartNavigate</c>. ChartStub.</summary>
    bool ChartNavigate(long chartId, int position, int shift = 0);

    /// <summary>MQL5 <c>ChartSetInteger</c>. ChartStub.</summary>
    bool ChartSetInteger(long chartId, int propertyId, long value);

    /// <summary>MQL5 <c>ChartSetInteger</c> with a sub-window. ChartStub.</summary>
    bool ChartSetInteger(long chartId, int propertyId, int subWindow, long value);

    /// <summary>MQL5 <c>ChartGetInteger</c>, direct-return form. ChartStub.</summary>
    long ChartGetInteger(long chartId, int propertyId, int subWindow = 0);

    /// <summary>MQL5 <c>ChartGetInteger</c>, out-parameter form. ChartStub.</summary>
    bool ChartGetInteger(long chartId, int propertyId, int subWindow, out long value);

    /// <summary>MQL5 <c>ChartSetDouble</c>. ChartStub.</summary>
    bool ChartSetDouble(long chartId, int propertyId, double value);

    /// <summary>MQL5 <c>ChartGetDouble</c>, direct-return form. ChartStub.</summary>
    double ChartGetDouble(long chartId, int propertyId, int subWindow = 0);

    /// <summary>MQL5 <c>ChartGetDouble</c>, out-parameter form. ChartStub.</summary>
    bool ChartGetDouble(long chartId, int propertyId, int subWindow, out double value);

    /// <summary>MQL5 <c>ChartSetString</c>. ChartStub.</summary>
    bool ChartSetString(long chartId, int propertyId, string? value);

    /// <summary>MQL5 <c>ChartGetString</c>, direct-return form. ChartStub.</summary>
    string ChartGetString(long chartId, int propertyId);

    /// <summary>MQL5 <c>ChartGetString</c>, out-parameter form. ChartStub.</summary>
    bool ChartGetString(long chartId, int propertyId, out string value);

    /// <summary>MQL5 <c>ChartIndicatorAdd</c>. ChartStub.</summary>
    bool ChartIndicatorAdd(long chartId, int subWindow, int indicatorHandle);

    /// <summary>MQL5 <c>ChartIndicatorDelete</c>. ChartStub.</summary>
    bool ChartIndicatorDelete(long chartId, int subWindow, string? indicatorShortName);

    /// <summary>MQL5 <c>ChartIndicatorGet</c>. ChartStub.</summary>
    int ChartIndicatorGet(long chartId, int subWindow, string? indicatorShortName);

    /// <summary>MQL5 <c>ChartIndicatorName</c>. ChartStub.</summary>
    string ChartIndicatorName(long chartId, int subWindow, int index);

    /// <summary>MQL5 <c>ChartIndicatorsTotal</c>. ChartStub.</summary>
    int ChartIndicatorsTotal(long chartId, int subWindow);

    /// <summary>MQL5 <c>ChartWindowFind</c>, the no-argument form. ChartStub.</summary>
    int ChartWindowFind();

    /// <summary>MQL5 <c>ChartWindowFind</c>, the named form. ChartStub.</summary>
    int ChartWindowFind(long chartId, string? indicatorShortName);

    /// <summary>MQL5 <c>ChartWindowOnDropped</c>. ChartStub.</summary>
    int ChartWindowOnDropped();

    /// <summary>MQL5 <c>ChartPriceOnDropped</c>. ChartStub.</summary>
    double ChartPriceOnDropped();

    /// <summary>MQL5 <c>ChartTimeOnDropped</c>. ChartStub.</summary>
    long ChartTimeOnDropped();

    /// <summary>MQL5 <c>ChartXOnDropped</c>. ChartStub.</summary>
    int ChartXOnDropped();

    /// <summary>MQL5 <c>ChartYOnDropped</c>. ChartStub.</summary>
    int ChartYOnDropped();

    /// <summary>MQL5 <c>ChartTimePriceToXY</c>. ChartStub; there is no pixel grid to map onto.</summary>
    bool ChartTimePriceToXY(long chartId, int subWindow, long time, double price, out int x, out int y);

    /// <summary>MQL5 <c>ChartXYToTimePrice</c>. ChartStub; there is no pixel grid to map from.</summary>
    bool ChartXYToTimePrice(long chartId, int x, int y, out int subWindow, out long time, out double price);

    /// <summary>MQL5 <c>ChartSaveTemplate</c>. Unsupported: it writes a template file.</summary>
    bool ChartSaveTemplate(long chartId, string? filename);

    /// <summary>MQL5 <c>ChartScreenShot</c>. Unsupported: it writes an image file.</summary>
    bool ChartScreenShot(long chartId, string? filename, int width, int height, int alignMode = 0);

    /// <summary>MQL5 <c>EventChartCustom</c>. ChartStub: there is no chart event loop here.</summary>
    bool EventChartCustom(long chartId, ushort customEventId, long lparam, double dparam, string? sparam);

    /// <summary>MQL5 <c>EventSetTimer</c>. EngineBound.</summary>
    bool EventSetTimer(int seconds);

    /// <summary>MQL5 <c>EventSetMillisecondTimer</c>. EngineBound.</summary>
    bool EventSetMillisecondTimer(int milliseconds);

    /// <summary>MQL5 <c>EventKillTimer</c>. EngineBound.</summary>
    void EventKillTimer();
}

public sealed partial class Mql5Runtime
{
    /// <inheritdoc />
    public bool ObjectCreate(long chartId, string? name, int type, int subWindow, long time1, double price1, long time2 = 0, double price2 = 0, long time3 = 0, double price3 = 0)
    {
        RecordChartCall(nameof(ObjectCreate));
        bool created = ChartObjects.Create(
            ResolveChartId(chartId),
            name ?? string.Empty,
            type,
            subWindow,
            [(time1, price1), (time2, price2), (time3, price3)]);

        if (!created)
        {
            SetError(Mql5ErrorCodes.ObjectError);
        }

        return created;
    }

    /// <inheritdoc />
    public bool ObjectDelete(long chartId, string? name)
    {
        RecordChartCall(nameof(ObjectDelete));
        bool deleted = ChartObjects.Delete(ResolveChartId(chartId), name ?? string.Empty);
        if (!deleted)
        {
            SetError(Mql5ErrorCodes.ObjectNotFound);
        }

        return deleted;
    }

    /// <inheritdoc />
    public int ObjectFind(long chartId, string? name)
    {
        RecordChartCall(nameof(ObjectFind));
        return ChartObjects.Find(ResolveChartId(chartId), name ?? string.Empty);
    }

    /// <inheritdoc />
    public bool ObjectMove(long chartId, string? name, int pointIndex, long time, double price)
    {
        RecordChartCall(nameof(ObjectMove));
        return ChartObjects.Move(ResolveChartId(chartId), name ?? string.Empty, pointIndex, time, price);
    }

    /// <inheritdoc />
    public string ObjectName(long chartId, int position, int subWindow = -1, int type = -1)
    {
        RecordChartCall(nameof(ObjectName));
        return ChartObjects.NameAt(ResolveChartId(chartId), position, subWindow, type);
    }

    /// <inheritdoc />
    public int ObjectsTotal(long chartId, int subWindow = -1, int type = -1)
    {
        RecordChartCall(nameof(ObjectsTotal));
        return ChartObjects.Total(ResolveChartId(chartId), subWindow, type);
    }

    /// <inheritdoc />
    public int ObjectsDeleteAll(long chartId, int subWindow = -1, int type = -1)
    {
        RecordChartCall(nameof(ObjectsDeleteAll));
        return ChartObjects.DeleteAll(ResolveChartId(chartId), prefix: null, subWindow, type);
    }

    /// <inheritdoc />
    public int ObjectsDeleteAll(long chartId, string? prefix, int subWindow = -1, int objectType = -1)
    {
        RecordChartCall(nameof(ObjectsDeleteAll));
        return ChartObjects.DeleteAll(ResolveChartId(chartId), prefix ?? string.Empty, subWindow, objectType);
    }

    /// <inheritdoc />
    public bool ObjectSetInteger(long chartId, string? name, int propertyId, long value)
        => ObjectSetInteger(chartId, name, propertyId, 0, value);

    /// <inheritdoc />
    public bool ObjectSetInteger(long chartId, string? name, int propertyId, int propertyModifier, long value)
    {
        RecordChartCall(nameof(ObjectSetInteger));
        bool ok = ChartObjects.SetInteger(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, value);
        if (!ok)
        {
            SetError(Mql5ErrorCodes.ObjectNotFound);
        }

        return ok;
    }

    /// <inheritdoc />
    public long ObjectGetInteger(long chartId, string? name, int propertyId, int propertyModifier = 0)
    {
        ChartObjects.TryGetInteger(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out long value);
        return value;
    }

    /// <inheritdoc />
    public bool ObjectGetInteger(long chartId, string? name, int propertyId, int propertyModifier, out long value)
        => ChartObjects.TryGetInteger(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out value);

    /// <inheritdoc />
    public bool ObjectSetDouble(long chartId, string? name, int propertyId, double value)
        => ObjectSetDouble(chartId, name, propertyId, 0, value);

    /// <inheritdoc />
    public bool ObjectSetDouble(long chartId, string? name, int propertyId, int propertyModifier, double value)
    {
        RecordChartCall(nameof(ObjectSetDouble));
        bool ok = ChartObjects.SetDouble(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, value);
        if (!ok)
        {
            SetError(Mql5ErrorCodes.ObjectNotFound);
        }

        return ok;
    }

    /// <inheritdoc />
    public double ObjectGetDouble(long chartId, string? name, int propertyId, int propertyModifier = 0)
    {
        ChartObjects.TryGetDouble(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out double value);
        return value;
    }

    /// <inheritdoc />
    public bool ObjectGetDouble(long chartId, string? name, int propertyId, int propertyModifier, out double value)
        => ChartObjects.TryGetDouble(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out value);

    /// <inheritdoc />
    public bool ObjectSetString(long chartId, string? name, int propertyId, string? value)
        => ObjectSetString(chartId, name, propertyId, 0, value);

    /// <inheritdoc />
    public bool ObjectSetString(long chartId, string? name, int propertyId, int propertyModifier, string? value)
    {
        RecordChartCall(nameof(ObjectSetString));
        bool ok = ChartObjects.SetString(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, value ?? string.Empty);
        if (!ok)
        {
            SetError(Mql5ErrorCodes.ObjectNotFound);
        }

        return ok;
    }

    /// <inheritdoc />
    public string ObjectGetString(long chartId, string? name, int propertyId, int propertyModifier = 0)
    {
        ChartObjects.TryGetString(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out string value);
        return value;
    }

    /// <inheritdoc />
    public bool ObjectGetString(long chartId, string? name, int propertyId, int propertyModifier, out string value)
        => ChartObjects.TryGetString(ResolveChartId(chartId), name ?? string.Empty, propertyId, propertyModifier, out value);

    /// <inheritdoc />
    public double ObjectGetValueByTime(long chartId, string? name, long time, int lineId = 0)
    {
        RecordChartCall(nameof(ObjectGetValueByTime));
        return 0;
    }

    /// <inheritdoc />
    public long ObjectGetTimeByValue(long chartId, string? name, double value, int lineId = 0)
    {
        RecordChartCall(nameof(ObjectGetTimeByValue));
        return 0;
    }

    /// <inheritdoc />
    public bool TextGetSize(string? text, out uint width, out uint height)
    {
        RecordChartCall(nameof(TextGetSize));

        // A nominal 7x16 pixel cell. There is no font engine here; what panel code
        // needs is a monotonic, non-zero measurement it can lay out against.
        width = (uint)((text?.Length ?? 0) * 7);
        height = 16;
        return true;
    }

    /// <inheritdoc />
    public bool TextOut(string? text, int x, int y, uint anchor, uint[]? data, uint width, uint height, uint color, int colorFormat)
    {
        RecordChartCall(nameof(TextOut));
        return true;
    }

    /// <inheritdoc />
    public bool TextSetFont(string? name, int size, uint flags = 0, int orientation = 0)
    {
        RecordChartCall(nameof(TextSetFont));
        return true;
    }

    /// <inheritdoc />
    public long ChartId()
    {
        RecordChartCall(nameof(ChartId));
        return ResolveChartId(0);
    }

    /// <inheritdoc />
    public long ChartFirst()
    {
        RecordChartCall(nameof(ChartFirst));
        return ResolveChartId(0);
    }

    /// <inheritdoc />
    public long ChartNext(long chartId)
    {
        RecordChartCall(nameof(ChartNext));
        return -1;
    }

    /// <inheritdoc />
    public long ChartOpen(string? symbol, int period)
    {
        RecordChartCall(nameof(ChartOpen));
        return ResolveChartId(0);
    }

    /// <inheritdoc />
    public bool ChartClose(long chartId = 0)
    {
        RecordChartCall(nameof(ChartClose));
        return true;
    }

    /// <inheritdoc />
    public void ChartRedraw(long chartId = 0) => RecordChartCall(nameof(ChartRedraw));

    /// <inheritdoc />
    public string ChartSymbol(long chartId = 0)
    {
        RecordChartCall(nameof(ChartSymbol));
        return context.Symbol;
    }

    /// <inheritdoc />
    public int ChartPeriod(long chartId = 0)
    {
        RecordChartCall(nameof(ChartPeriod));
        return context.Period;
    }

    /// <inheritdoc />
    public bool ChartSetSymbolPeriod(long chartId, string? symbol, int period)
    {
        RecordChartCall(nameof(ChartSetSymbolPeriod));
        return true;
    }

    /// <inheritdoc />
    public bool ChartApplyTemplate(long chartId, string? filename)
    {
        RecordChartCall(nameof(ChartApplyTemplate));
        return true;
    }

    /// <inheritdoc />
    public bool ChartNavigate(long chartId, int position, int shift = 0)
    {
        RecordChartCall(nameof(ChartNavigate));
        return true;
    }

    /// <inheritdoc />
    public bool ChartSetInteger(long chartId, int propertyId, long value) => ChartSetInteger(chartId, propertyId, 0, value);

    /// <inheritdoc />
    public bool ChartSetInteger(long chartId, int propertyId, int subWindow, long value)
    {
        RecordChartCall(nameof(ChartSetInteger));
        ChartObjects.SetChartInteger(ResolveChartId(chartId), propertyId, subWindow, value);
        return true;
    }

    /// <inheritdoc />
    public long ChartGetInteger(long chartId, int propertyId, int subWindow = 0)
    {
        ChartObjects.TryGetChartInteger(ResolveChartId(chartId), propertyId, subWindow, out long value);
        return value;
    }

    /// <inheritdoc />
    public bool ChartGetInteger(long chartId, int propertyId, int subWindow, out long value)
        => ChartObjects.TryGetChartInteger(ResolveChartId(chartId), propertyId, subWindow, out value);

    /// <inheritdoc />
    public bool ChartSetDouble(long chartId, int propertyId, double value)
    {
        RecordChartCall(nameof(ChartSetDouble));
        ChartObjects.SetChartDouble(ResolveChartId(chartId), propertyId, 0, value);
        return true;
    }

    /// <inheritdoc />
    public double ChartGetDouble(long chartId, int propertyId, int subWindow = 0)
    {
        ChartObjects.TryGetChartDouble(ResolveChartId(chartId), propertyId, subWindow, out double value);
        return value;
    }

    /// <inheritdoc />
    public bool ChartGetDouble(long chartId, int propertyId, int subWindow, out double value)
        => ChartObjects.TryGetChartDouble(ResolveChartId(chartId), propertyId, subWindow, out value);

    /// <inheritdoc />
    public bool ChartSetString(long chartId, int propertyId, string? value)
    {
        RecordChartCall(nameof(ChartSetString));
        ChartObjects.SetChartString(ResolveChartId(chartId), propertyId, value ?? string.Empty);
        return true;
    }

    /// <inheritdoc />
    public string ChartGetString(long chartId, int propertyId)
    {
        ChartObjects.TryGetChartString(ResolveChartId(chartId), propertyId, out string value);
        return value;
    }

    /// <inheritdoc />
    public bool ChartGetString(long chartId, int propertyId, out string value)
        => ChartObjects.TryGetChartString(ResolveChartId(chartId), propertyId, out value);

    /// <inheritdoc />
    public bool ChartIndicatorAdd(long chartId, int subWindow, int indicatorHandle)
    {
        RecordChartCall(nameof(ChartIndicatorAdd));
        return true;
    }

    /// <inheritdoc />
    public bool ChartIndicatorDelete(long chartId, int subWindow, string? indicatorShortName)
    {
        RecordChartCall(nameof(ChartIndicatorDelete));
        return true;
    }

    /// <inheritdoc />
    public int ChartIndicatorGet(long chartId, int subWindow, string? indicatorShortName)
    {
        RecordChartCall(nameof(ChartIndicatorGet));
        return Mql5Constants.InvalidHandle;
    }

    /// <inheritdoc />
    public string ChartIndicatorName(long chartId, int subWindow, int index)
    {
        RecordChartCall(nameof(ChartIndicatorName));
        return string.Empty;
    }

    /// <inheritdoc />
    public int ChartIndicatorsTotal(long chartId, int subWindow)
    {
        RecordChartCall(nameof(ChartIndicatorsTotal));
        return 0;
    }

    /// <inheritdoc />
    public int ChartWindowFind()
    {
        RecordChartCall(nameof(ChartWindowFind));
        return 0;
    }

    /// <inheritdoc />
    public int ChartWindowFind(long chartId, string? indicatorShortName)
    {
        RecordChartCall(nameof(ChartWindowFind));
        return -1;
    }

    /// <inheritdoc />
    public int ChartWindowOnDropped()
    {
        RecordChartCall(nameof(ChartWindowOnDropped));
        return 0;
    }

    /// <inheritdoc />
    public double ChartPriceOnDropped()
    {
        RecordChartCall(nameof(ChartPriceOnDropped));
        return 0;
    }

    /// <inheritdoc />
    public long ChartTimeOnDropped()
    {
        RecordChartCall(nameof(ChartTimeOnDropped));
        return 0;
    }

    /// <inheritdoc />
    public int ChartXOnDropped()
    {
        RecordChartCall(nameof(ChartXOnDropped));
        return 0;
    }

    /// <inheritdoc />
    public int ChartYOnDropped()
    {
        RecordChartCall(nameof(ChartYOnDropped));
        return 0;
    }

    /// <inheritdoc />
    public bool ChartTimePriceToXY(long chartId, int subWindow, long time, double price, out int x, out int y)
    {
        RecordChartCall(nameof(ChartTimePriceToXY));
        x = 0;
        y = 0;
        return false;
    }

    /// <inheritdoc />
    public bool ChartXYToTimePrice(long chartId, int x, int y, out int subWindow, out long time, out double price)
    {
        RecordChartCall(nameof(ChartXYToTimePrice));
        subWindow = 0;
        time = 0;
        price = 0;
        return false;
    }

    /// <inheritdoc />
    public bool ChartSaveTemplate(long chartId, string? filename)
        => throw Refuse(nameof(ChartSaveTemplate), "it writes a template file into the terminal sandbox");

    /// <inheritdoc />
    public bool ChartScreenShot(long chartId, string? filename, int width, int height, int alignMode = 0)
        => throw Refuse(nameof(ChartScreenShot), "it writes an image file into the terminal sandbox");

    /// <inheritdoc />
    public bool EventChartCustom(long chartId, ushort customEventId, long lparam, double dparam, string? sparam)
    {
        RecordChartCall(nameof(EventChartCustom));
        return true;
    }

    /// <inheritdoc />
    public bool EventSetTimer(int seconds) => context.EventSetTimer(seconds);

    /// <inheritdoc />
    public bool EventSetMillisecondTimer(int milliseconds) => context.EventSetMillisecondTimer(milliseconds);

    /// <inheritdoc />
    public void EventKillTimer() => context.EventKillTimer();
}
