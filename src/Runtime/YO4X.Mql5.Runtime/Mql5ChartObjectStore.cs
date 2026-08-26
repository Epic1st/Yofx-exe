namespace YO4X.Mql5.Runtime;

/// <summary>
/// One graphical object a strategy asked the terminal to draw.
///
/// Nothing here reaches a screen. The record exists so that a strategy which writes
/// a property and reads it back gets its own value, which is what a great deal of
/// MQL5 panel code does - it stores state on button objects and reads it on the next
/// tick.
/// </summary>
public sealed class Mql5ChartObject
{
    internal Mql5ChartObject(string name, int type, int subWindow)
    {
        Name = name;
        Type = type;
        SubWindow = subWindow;
    }

    /// <summary>The object name, unique within its chart.</summary>
    public string Name { get; }

    /// <summary>The <c>ENUM_OBJECT</c> member the object was created with.</summary>
    public int Type { get; }

    /// <summary>The chart sub-window the object was created in.</summary>
    public int SubWindow { get; }

    /// <summary>The anchor points, as time/price pairs, in the order MQL5 numbers them.</summary>
    public List<(long Time, double Price)> Anchors { get; } = [];

    internal Dictionary<(int PropertyId, int Modifier), long> Integers { get; } = [];

    internal Dictionary<(int PropertyId, int Modifier), double> Doubles { get; } = [];

    internal Dictionary<(int PropertyId, int Modifier), string> Strings { get; } = [];
}

/// <summary>
/// The recording backing store for the <c>Object*</c> and <c>Chart*</c> built-ins.
///
/// These built-ins are visual only. A backtest has no chart, so drawing is a no-op -
/// but a blind no-op is not good enough: MQL5 dashboard code creates an object, sets
/// properties on it, later reads them back, counts objects with
/// <c>ObjectsTotal</c> and deletes them by prefix. A store that forgets everything
/// makes that code take branches it would never take on a terminal.
///
/// So every call is recorded and every read is answered from the recording. What is
/// lost is only the pixels.
/// </summary>
public sealed class Mql5ChartObjectStore
{
    private readonly Dictionary<long, ChartState> charts = [];

    /// <summary>The number of <c>Object*</c> and <c>Chart*</c> calls recorded.</summary>
    public long CallCount { get; private set; }

    /// <summary>Every object currently held for <paramref name="chartId"/>, in creation order.</summary>
    public IReadOnlyList<Mql5ChartObject> Objects(long chartId)
        => charts.TryGetValue(chartId, out ChartState? state) ? state.Ordered : [];

    internal void Record() => CallCount++;

    internal bool Create(long chartId, string name, int type, int subWindow, IReadOnlyList<(long Time, double Price)> anchors)
    {
        Record();
        ChartState state = State(chartId);
        if (state.Objects.ContainsKey(name))
        {
            return false;
        }

        Mql5ChartObject created = new(name, type, subWindow);
        created.Anchors.AddRange(anchors);
        state.Objects[name] = created;
        state.Ordered.Add(created);
        return true;
    }

    internal bool Delete(long chartId, string name)
    {
        Record();
        if (!charts.TryGetValue(chartId, out ChartState? state) || !state.Objects.Remove(name, out Mql5ChartObject? removed))
        {
            return false;
        }

        state.Ordered.Remove(removed);
        return true;
    }

    internal int Find(long chartId, string name)
    {
        Record();
        return charts.TryGetValue(chartId, out ChartState? state) && state.Objects.TryGetValue(name, out Mql5ChartObject? found)
            ? found.SubWindow
            : -1;
    }

    internal bool Move(long chartId, string name, int pointIndex, long time, double price)
    {
        Record();
        if (pointIndex < 0 || !charts.TryGetValue(chartId, out ChartState? state) || !state.Objects.TryGetValue(name, out Mql5ChartObject? found))
        {
            return false;
        }

        while (found.Anchors.Count <= pointIndex)
        {
            found.Anchors.Add((0, 0));
        }

        found.Anchors[pointIndex] = (time, price);
        return true;
    }

    internal string NameAt(long chartId, int position, int subWindow, int type)
    {
        Record();
        List<Mql5ChartObject> matches = Matching(chartId, subWindow, type);
        return position >= 0 && position < matches.Count ? matches[position].Name : string.Empty;
    }

    internal int Total(long chartId, int subWindow, int type)
    {
        Record();
        return Matching(chartId, subWindow, type).Count;
    }

    internal int DeleteAll(long chartId, string? prefix, int subWindow, int type)
    {
        Record();
        if (!charts.TryGetValue(chartId, out ChartState? state))
        {
            return 0;
        }

        List<Mql5ChartObject> doomed = [];
        foreach (Mql5ChartObject candidate in state.Ordered)
        {
            if (subWindow >= 0 && candidate.SubWindow != subWindow)
            {
                continue;
            }

            if (type >= 0 && candidate.Type != type)
            {
                continue;
            }

            if (prefix is not null && !candidate.Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            doomed.Add(candidate);
        }

        foreach (Mql5ChartObject candidate in doomed)
        {
            state.Objects.Remove(candidate.Name);
            state.Ordered.Remove(candidate);
        }

        return doomed.Count;
    }

    internal bool SetInteger(long chartId, string name, int propertyId, int modifier, long value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is null)
        {
            return false;
        }

        target.Integers[(propertyId, modifier)] = value;
        return true;
    }

    internal bool TryGetInteger(long chartId, string name, int propertyId, int modifier, out long value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is not null && target.Integers.TryGetValue((propertyId, modifier), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal bool SetDouble(long chartId, string name, int propertyId, int modifier, double value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is null)
        {
            return false;
        }

        target.Doubles[(propertyId, modifier)] = value;
        return true;
    }

    internal bool TryGetDouble(long chartId, string name, int propertyId, int modifier, out double value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is not null && target.Doubles.TryGetValue((propertyId, modifier), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal bool SetString(long chartId, string name, int propertyId, int modifier, string value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is null)
        {
            return false;
        }

        target.Strings[(propertyId, modifier)] = value;
        return true;
    }

    internal bool TryGetString(long chartId, string name, int propertyId, int modifier, out string value)
    {
        Record();
        Mql5ChartObject? target = Lookup(chartId, name);
        if (target is not null && target.Strings.TryGetValue((propertyId, modifier), out string? found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal void SetChartInteger(long chartId, int propertyId, int subWindow, long value)
    {
        Record();
        State(chartId).Integers[(propertyId, subWindow)] = value;
    }

    internal bool TryGetChartInteger(long chartId, int propertyId, int subWindow, out long value)
    {
        Record();
        if (charts.TryGetValue(chartId, out ChartState? state) && state.Integers.TryGetValue((propertyId, subWindow), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal void SetChartDouble(long chartId, int propertyId, int subWindow, double value)
    {
        Record();
        State(chartId).Doubles[(propertyId, subWindow)] = value;
    }

    internal bool TryGetChartDouble(long chartId, int propertyId, int subWindow, out double value)
    {
        Record();
        if (charts.TryGetValue(chartId, out ChartState? state) && state.Doubles.TryGetValue((propertyId, subWindow), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    internal void SetChartString(long chartId, int propertyId, string value)
    {
        Record();
        State(chartId).Strings[propertyId] = value;
    }

    internal bool TryGetChartString(long chartId, int propertyId, out string value)
    {
        Record();
        if (charts.TryGetValue(chartId, out ChartState? state) && state.Strings.TryGetValue(propertyId, out string? found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private Mql5ChartObject? Lookup(long chartId, string name)
        => charts.TryGetValue(chartId, out ChartState? state) && state.Objects.TryGetValue(name, out Mql5ChartObject? found) ? found : null;

    private List<Mql5ChartObject> Matching(long chartId, int subWindow, int type)
    {
        if (!charts.TryGetValue(chartId, out ChartState? state))
        {
            return [];
        }

        if (subWindow < 0 && type < 0)
        {
            return state.Ordered;
        }

        List<Mql5ChartObject> matches = [];
        foreach (Mql5ChartObject candidate in state.Ordered)
        {
            if (subWindow >= 0 && candidate.SubWindow != subWindow)
            {
                continue;
            }

            if (type >= 0 && candidate.Type != type)
            {
                continue;
            }

            matches.Add(candidate);
        }

        return matches;
    }

    private ChartState State(long chartId)
    {
        if (!charts.TryGetValue(chartId, out ChartState? state))
        {
            state = new ChartState();
            charts[chartId] = state;
        }

        return state;
    }

    private sealed class ChartState
    {
        public Dictionary<string, Mql5ChartObject> Objects { get; } = new(StringComparer.Ordinal);

        public List<Mql5ChartObject> Ordered { get; } = [];

        public Dictionary<(int PropertyId, int Modifier), long> Integers { get; } = [];

        public Dictionary<(int PropertyId, int Modifier), double> Doubles { get; } = [];

        public Dictionary<int, string> Strings { get; } = [];
    }
}
