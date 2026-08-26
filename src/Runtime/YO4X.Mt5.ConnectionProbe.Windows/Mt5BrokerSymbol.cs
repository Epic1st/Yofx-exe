namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// One instrument as the broker describes it.
///
/// <para>
/// Every field except the name is nullable because brokers differ in what they populate, and
/// a missing figure has to stay missing. A zero contract size or a zero digit count would be
/// read downstream as a measurement, and sizing an order from an invented number is exactly
/// the failure this type exists to prevent.
/// </para>
/// </summary>
/// <param name="Symbol">The instrument name, as the broker spells it.</param>
/// <param name="Description">The broker's own description, when it gives one.</param>
/// <param name="Digits">Price precision.</param>
/// <param name="ContractSize">Units per lot.</param>
/// <param name="Currency">The instrument's currency.</param>
/// <param name="TickSize">The smallest price increment.</param>
/// <param name="TickValue">What one tick is worth.</param>
public sealed record Mt5BrokerSymbol(
    string Symbol,
    string? Description,
    int? Digits,
    decimal? ContractSize,
    string? Currency,
    decimal? TickSize,
    decimal? TickValue);
