namespace YO4X.Mql5.Runtime;

/// <summary>
/// The executable shape of a compiled MQL5 program: the three entry points a
/// terminal calls.
///
/// A generated strategy implements this and holds its <see cref="IMql5Runtime"/>
/// as a constructor-injected field, so the entry points take no parameters. This
/// is the contract the code generator emits against; the runtime owns it because
/// both the generator and any host must agree on one declaration.
/// </summary>
public interface IMql5Strategy
{
    /// <summary>
    /// Runs the module's <c>OnInit</c> handler. An MQL5 return code, if the source
    /// declared one, is discarded here — a host that needs it should read the
    /// strategy's own <c>OnInit</c> method directly.
    /// </summary>
    void OnInit();

    /// <summary>Runs the module's <c>OnTick</c> handler.</summary>
    void OnTick();

    /// <summary>Runs the module's <c>OnDeinit</c> handler.</summary>
    /// <param name="reason">The MQL5 deinitialisation reason code.</param>
    void OnDeinit(int reason);
}
