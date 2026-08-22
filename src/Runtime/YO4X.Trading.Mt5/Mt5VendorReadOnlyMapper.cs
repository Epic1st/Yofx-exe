using System.Globalization;
using mtapi.mt5;
using YO4X.Trading.Abstractions;

namespace YO4X.Trading.Mt5;

/// <summary>
/// Narrow compile-time binding to the documented vendor surface. These methods only read
/// already-populated in-memory values. They never connect, subscribe, request history, or trade.
/// </summary>
public static class Mt5VendorReadOnlyMapper
{
    public static GatewayConnectionState MapConnectionState(MT5API api)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.Connected
            ? GatewayConnectionState.Connected
            : GatewayConnectionState.Disconnected;
    }

    public static Mt5VendorAccountObservation MapAccount(
        MT5API api,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(api);
        return new Mt5VendorAccountObservation(
            MaskLogin(api.User),
            RequireText(api.AccountCompanyName, "account_company_missing"),
            RequireText(api.AccountCurrency, "account_currency_missing"),
            MapAccountMode(api.AccountMethod),
            ToDecimal(api.AccountEquity, "account_equity_invalid"),
            ToDecimal(api.AccountFreeMargin, "account_free_margin_invalid"),
            ToDecimal(api.AccountMargin, "account_margin_invalid"),
            observedAtUtc.ToUniversalTime());
    }

    /// <summary>
    /// Maps a vendor quote only when the caller has already normalized the broker timestamp.
    /// The vendor XML describes <c>Quote.Time</c> as server time without a timezone contract,
    /// so this boundary deliberately does not infer UTC from that field.
    /// </summary>
    public static BrokerQuoteSnapshot MapQuote(
        Quote quote,
        long sequence,
        DateTimeOffset normalizedBrokerTimestampUtc,
        DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        decimal bid = ToDecimal(quote.Bid, "quote_bid_invalid");
        decimal ask = ToDecimal(quote.Ask, "quote_ask_invalid");
        if (bid <= decimal.Zero || ask <= decimal.Zero || ask < bid)
        {
            throw new InvalidDataException("The vendor quote is not a valid positive bid/ask observation.");
        }

        return new BrokerQuoteSnapshot(
            sequence,
            RequireText(quote.Symbol, "quote_symbol_missing"),
            bid,
            ask,
            normalizedBrokerTimestampUtc.ToUniversalTime(),
            receivedAtUtc.ToUniversalTime());
    }

    private static BrokerAccountMode MapAccountMode(object accountMethod)
    {
        string value = Convert.ToString(accountMethod, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value.Contains("hedg", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Hedging;
        }

        if (value.Contains("net", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Netting;
        }

        if (value.Contains("exchange", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerAccountMode.Exchange;
        }

        return BrokerAccountMode.Unknown;
    }

    private static string MaskLogin(object login)
    {
        string value = Convert.ToString(login, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidDataException("The vendor account login is missing.");
        }

        int visibleLength = Math.Min(4, value.Length);
        return string.Concat(new string('*', value.Length - visibleLength), value[^visibleLength..]);
    }

    private static decimal ToDecimal(object value, string failureCode)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(failureCode, exception);
        }
    }

    private static string RequireText(string? value, string failureCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(failureCode);
        }

        return value.Trim();
    }
}

public sealed record Mt5VendorAccountObservation(
    string MaskedLogin,
    string BrokerCompany,
    string Currency,
    BrokerAccountMode AccountMode,
    decimal Equity,
    decimal FreeMargin,
    decimal UsedMargin,
    DateTimeOffset ObservedAtUtc);
