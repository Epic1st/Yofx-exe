namespace YO4X.Mql5.Runtime;

/// <summary>
/// Produces the zeroed instance <c>ZeroMemory</c> is defined to leave behind for an MQL5
/// structure.
/// </summary>
/// <remarks>
/// MQL5 structures are value types. Translated code represents them as CLR classes, so the
/// zeroing has to be modelled rather than inherited: a newly constructed instance has every
/// field at its default, which is what <c>ZeroMemory</c> guarantees.
///
/// The constructor is resolved once per type and cached in the static field, because
/// <c>ZeroMemory</c> is called on the hot path of any strategy that builds a trade request per
/// tick. A type with no accessible parameterless constructor cannot be zeroed this way; that is
/// reported when it is attempted rather than papered over with a null, because a null here
/// surfaces as a <see cref="NullReferenceException"/> inside translated code, where it reads as
/// a fault in the strategy rather than in the runtime.
/// </remarks>
internal static class Mql5ZeroedInstance<T>
{
    private static readonly Func<T>? Factory = BuildFactory();

    /// <summary>Returns a fresh instance with every field at its default.</summary>
    public static T Create()
    {
        if (Factory is null)
        {
            throw new Mql5UnsupportedOperationException(
                "ZeroMemory cannot zero a value of type '" + typeof(T).FullName
                    + "' because it has no accessible parameterless constructor.");
        }

        return Factory();
    }

    private static Func<T>? BuildFactory()
    {
        if (typeof(T).IsAbstract || typeof(T).IsInterface || typeof(T).IsArray)
        {
            return null;
        }

        return typeof(T).GetConstructor(Type.EmptyTypes) is null
            ? null
            : static () => Activator.CreateInstance<T>();
    }
}
