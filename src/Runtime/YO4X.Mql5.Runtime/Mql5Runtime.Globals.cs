using System.Diagnostics.CodeAnalysis;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// MQL5's global variables of the terminal, implemented as <b>Native</b> per-run storage.
///
/// This family used to be refused wholesale, on the grounds that a terminal global
/// outlives the program and is shared across the whole installation. That is true of a
/// running terminal and false of a test run, which is the only mode this engine has.
/// MetaQuotes documents the difference in the Testing Trading Strategies reference:
/// "During testing, the global variables of the client terminal are also emulated, but
/// they are not related to the current global variables of the terminal ... All
/// operations with the global variables of the terminal, during testing, take place
/// outside of the client terminal (in the testing agent)." Providing them is therefore
/// part of what a strategy tester is, and the old refusal was refusing a facility the
/// model this engine implements actually has. Every corpus use is the same shape - an
/// EA parking its own lot size, streak counter or basket state under a private name
/// prefix - which is exactly what the tester emulation exists for.
///
/// <b>Reproducibility.</b> The store belongs to one <see cref="Mql5Runtime"/>, and one
/// runtime instance is one strategy run, so the set starts empty on every run and dies
/// with it. Nothing is written anywhere. That is stricter than a MetaTrader agent,
/// which keeps its emulated set alive between passes - which is in turn why
/// MetaQuotes' own guidance is to delete the variables in <c>OnInit</c>. Starting
/// empty hands every run the state that guidance is trying to produce, and it is the
/// only member of that family of behaviours that replays identically.
///
/// <b>Timestamps.</b> <c>GlobalVariableSet</c> and <c>GlobalVariableTime</c> report the
/// simulated clock (<c>TimeCurrent</c>), never the wall clock, for the same reason the
/// tick counters do: a wall clock makes the same bars answer differently on every
/// replay.
///
/// <b>Temporary variables.</b> <c>GlobalVariableTemp</c> creates one that MetaTrader
/// would keep out of its on-disk set and drop at terminal shutdown. Here nothing is
/// ever written to disk and everything is dropped at the end of the run, so a
/// temporary and a permanent variable are indistinguishable and the distinction is not
/// tracked.
/// </summary>
public partial interface IMql5Runtime
{
    /// <summary>
    /// MQL5 <c>GlobalVariableCheck</c>. True when the run has a global variable of that
    /// name. A hit counts as an access and moves the variable's time. Native.
    /// </summary>
    bool GlobalVariableCheck(string? name);

    /// <summary>MQL5 <c>GlobalVariableDel</c>. False when there is nothing to delete. Native.</summary>
    bool GlobalVariableDel(string? name);

    /// <summary>
    /// MQL5 <c>GlobalVariableGet</c>, direct-return form. Returns 0 and sets
    /// <c>ERR_GLOBALVARIABLE_NOT_FOUND</c> when the variable does not exist, which is
    /// indistinguishable from a stored 0 - the reason MQL5 also offers the
    /// out-parameter form. Native.
    /// </summary>
    double GlobalVariableGet(string? name);

    /// <summary>MQL5 <c>GlobalVariableGet</c>, out-parameter form. Native.</summary>
    bool GlobalVariableGet(string? name, out double value);

    /// <summary>
    /// MQL5 <c>GlobalVariableName</c>. Names are indexed in creation order, so deleting
    /// index <c>i</c> leaves every lower index where it was. Native.
    /// </summary>
    string GlobalVariableName(int index);

    /// <summary>
    /// MQL5 <c>GlobalVariableSet</c>. Creates the variable if it is absent and returns
    /// the modification time, or 0 on failure. Native.
    /// </summary>
    long GlobalVariableSet(string? name, double value);

    /// <summary>MQL5 <c>GlobalVariablesTotal</c>. Native.</summary>
    int GlobalVariablesTotal();

    /// <summary>MQL5 <c>GlobalVariableTime</c>. The time of last access, or 0 when absent. Native.</summary>
    long GlobalVariableTime(string? name);

    /// <summary>
    /// MQL5 <c>GlobalVariableTemp</c>. Creates a variable holding 0, and fails when one
    /// of that name already exists. Native.
    /// </summary>
    bool GlobalVariableTemp(string? name);

    /// <summary>
    /// MQL5 <c>GlobalVariableSetOnCondition</c>. Assigns only when the stored value
    /// still equals <paramref name="checkValue"/>. Native.
    /// </summary>
    bool GlobalVariableSetOnCondition(string? name, double value, double checkValue);

    /// <summary>MQL5 <c>GlobalVariablesFlush</c>. Nothing is written anywhere, so this does nothing. Native.</summary>
    void GlobalVariablesFlush();

    /// <summary>
    /// MQL5 <c>GlobalVariablesDeleteAll</c>. Deletes the variables matching
    /// <paramref name="prefixName"/> and, when <paramref name="limitData"/> is non-zero,
    /// last touched before it. Returns how many went. Native.
    /// </summary>
    int GlobalVariablesDeleteAll(string? prefixName = null, long limitData = 0);
}

public sealed partial class Mql5Runtime
{
    /// <summary>
    /// MQL5 documents "A global variable name should not exceed 63 characters" on
    /// <c>GlobalVariableSet</c>. The limit is enforced rather than ignored, because a
    /// name this runtime accepts and MetaTrader rejects would make the two disagree
    /// about whether the strategy has any stored state at all.
    /// </summary>
    private const int MaxGlobalVariableNameLength = 63;

    private readonly Dictionary<string, GlobalVariableEntry> globalVariables = new(StringComparer.Ordinal);

    // Creation order, kept explicitly rather than read off the dictionary. The corpus
    // walks the set downwards while deleting from it -
    //   for(int i=GlobalVariablesTotal()-1;i>=0;i--) GlobalVariableDel(GlobalVariableName(i));
    // - which only visits every name if removing index i leaves the lower indices
    // alone. A list gives that; dictionary enumeration order is not a contract.
    private readonly List<string> globalVariableOrder = [];

    /// <inheritdoc />
    public bool GlobalVariableCheck(string? name)
    {
        GlobalVariableEntry? entry = FindGlobalVariable(name);
        if (entry is null)
        {
            return false;
        }

        // An existence probe counts as an access. The GlobalVariableTime reference says
        // so outright: "Addressing a variable for its value, for example using the
        // GlobalVariableGet() and GlobalVariableCheck() functions, also modifies the
        // time of last access."
        entry.LastAccess = TimeCurrent();
        return true;
    }

    /// <inheritdoc />
    public bool GlobalVariableDel(string? name)
    {
        if (name is null || !globalVariables.Remove(name))
        {
            SetError(Mql5ErrorCodes.GlobalVariableNotFound);
            return false;
        }

        globalVariableOrder.Remove(name);
        return true;
    }

    /// <inheritdoc />
    public double GlobalVariableGet(string? name)
    {
        GlobalVariableEntry? entry = FindGlobalVariable(name);
        if (entry is null)
        {
            return 0;
        }

        entry.LastAccess = TimeCurrent();
        return entry.Value;
    }

    /// <inheritdoc />
    public bool GlobalVariableGet(string? name, out double value)
    {
        GlobalVariableEntry? entry = FindGlobalVariable(name);
        if (entry is null)
        {
            value = 0;
            return false;
        }

        entry.LastAccess = TimeCurrent();
        value = entry.Value;
        return true;
    }

    /// <inheritdoc />
    public string GlobalVariableName(int index)
    {
        if (index < 0 || index >= globalVariableOrder.Count)
        {
            SetError(Mql5ErrorCodes.InvalidParameter);
            return string.Empty;
        }

        return globalVariableOrder[index];
    }

    /// <inheritdoc />
    public long GlobalVariableSet(string? name, double value)
    {
        if (!IsUsableGlobalVariableName(name))
        {
            SetError(Mql5ErrorCodes.InvalidParameter);
            return 0;
        }

        // The simulated clock, so the value replays. MQL5 reads 0 as failure here; a run
        // positioned exactly at the epoch would therefore report failure on a set that
        // worked, which is the same reading MQL5 gives a datetime of 0 everywhere else
        // and is not reachable from any real price history.
        long now = TimeCurrent();

        if (globalVariables.TryGetValue(name, out GlobalVariableEntry? existing))
        {
            existing.Value = value;
            existing.LastAccess = now;
            return now;
        }

        globalVariables.Add(name, new GlobalVariableEntry { Value = value, LastAccess = now });
        globalVariableOrder.Add(name);
        return now;
    }

    /// <inheritdoc />
    public int GlobalVariablesTotal() => globalVariableOrder.Count;

    /// <inheritdoc />
    public long GlobalVariableTime(string? name)
    {
        // Reading the timestamp is not "addressing a variable for its value", so unlike
        // Get and Check this one does not move the time it reports.
        GlobalVariableEntry? entry = FindGlobalVariable(name);
        return entry is null ? 0 : entry.LastAccess;
    }

    /// <inheritdoc />
    public bool GlobalVariableTemp(string? name)
    {
        if (!IsUsableGlobalVariableName(name))
        {
            SetError(Mql5ErrorCodes.InvalidParameter);
            return false;
        }

        if (globalVariables.ContainsKey(name))
        {
            // MetaQuotes does not spell out this failure, but ERR_GLOBALVARIABLE_EXISTS
            // (4502) is published as "Global variable of the client terminal with the
            // same name already exists", and GlobalVariableTemp is the only built-in in
            // the family that creates rather than assigns - so it is the only call that
            // can raise it.
            SetError(Mql5ErrorCodes.GlobalVariableExists);
            return false;
        }

        globalVariables.Add(name, new GlobalVariableEntry { Value = 0, LastAccess = TimeCurrent() });
        globalVariableOrder.Add(name);
        return true;
    }

    /// <inheritdoc />
    public bool GlobalVariableSetOnCondition(string? name, double value, double checkValue)
    {
        GlobalVariableEntry? entry = FindGlobalVariable(name);
        if (entry is null)
        {
            return false;
        }

        // Exact comparison is what MQL5 specifies, and it is the right thing: this call
        // exists as a mutex primitive, so both sides are flags a strategy wrote itself,
        // never measured prices. (The mutex has nothing to contend with here - one
        // runtime instance is one program - but the read-compare-write is still what the
        // caller's state machine expects.)
        if (!entry.Value.Equals(checkValue))
        {
            // ERR_GLOBALVARIABLE_NOT_MODIFIED (4503) is published as "Global variable of
            // the client terminal has not been modified", which is precisely this
            // outcome and nothing else in the family produces it. Leaving 4501 here
            // instead would be actively harmful: the usual idiom reads NOT_FOUND as
            // "create it", and the variable does exist.
            SetError(Mql5ErrorCodes.GlobalVariableNotModified);
            return false;
        }

        entry.Value = value;
        entry.LastAccess = TimeCurrent();
        return true;
    }

    /// <inheritdoc />
    public void GlobalVariablesFlush()
    {
        // Deliberately empty. MQL5 flushes the set to disk; this store has no disk
        // behind it, so there is nothing to force out and nothing for the caller to lose
        // by the call doing nothing.
    }

    /// <inheritdoc />
    public int GlobalVariablesDeleteAll(string? prefixName = null, long limitData = 0)
    {
        List<string> removed = [];

        foreach (string name in globalVariableOrder)
        {
            if (!string.IsNullOrEmpty(prefixName) && !name.StartsWith(prefixName, StringComparison.Ordinal))
            {
                continue;
            }

            // MQL5 describes limit_data as "variables which were changed before this
            // date". This store carries one timestamp per variable, moved by both writes
            // and reads, exactly as GlobalVariableTime reports it; a variable read since
            // the cut-off therefore survives. Zero means no date filter at all.
            if (limitData != 0 && globalVariables[name].LastAccess >= limitData)
            {
                continue;
            }

            removed.Add(name);
        }

        foreach (string name in removed)
        {
            globalVariables.Remove(name);
            globalVariableOrder.Remove(name);
        }

        return removed.Count;
    }

    /// <summary>
    /// The entry for <paramref name="name"/>, or null with
    /// <c>ERR_GLOBALVARIABLE_NOT_FOUND</c> recorded. Every read in the family reports a
    /// miss the same way, so the lookup owns the error rather than each caller.
    /// </summary>
    private GlobalVariableEntry? FindGlobalVariable(string? name)
    {
        if (name is not null && globalVariables.TryGetValue(name, out GlobalVariableEntry? entry))
        {
            return entry;
        }

        SetError(Mql5ErrorCodes.GlobalVariableNotFound);
        return null;
    }

    private static bool IsUsableGlobalVariableName([NotNullWhen(true)] string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= MaxGlobalVariableNameLength;

    /// <summary>One global variable: its value and the time it was last touched.</summary>
    private sealed class GlobalVariableEntry
    {
        public double Value { get; set; }

        public long LastAccess { get; set; }
    }
}
