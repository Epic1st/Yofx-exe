namespace YO4X.ControlPlane.Application;

public enum BotStatus
{
    Draft,
    Starting,
    Running,
    Paused,
    Stopped,
    Faulted
}

public enum BotHost
{
    Local,
    Cloud
}

public enum BacktestStatus
{
    Queued,
    Running,
    Complete,
    Failed
}

public enum CloudRunnerStatus
{
    Provisioning,
    Active,
    Suspended,
    Cancelled
}

public enum TradeSide
{
    Buy,
    Sell
}

public enum TrendDirection
{
    Up,
    Down,
    Flat
}

public sealed record StrategyCatalogQuery(
    int Page,
    int PageSize,
    string? Category,
    string? Symbol,
    string? Query,
    string? Sort);

public sealed record StrategyCatalogItem(
    Guid Id,
    string Slug,
    string Name,
    string AuthorName,
    string AuthorInitials,
    string Category,
    string Symbol,
    string Timeframe,
    string Version,
    decimal RatingAverage,
    int RatingCount,
    int ActiveUsers,
    bool IsFree,
    int CloudPriceMonthlyCents,
    int CloudPriceYearlyCents,
    string Currency,
    DateTimeOffset UpdatedAt);

public sealed record StrategyCatalogPage(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<StrategyCatalogItem> Items,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Symbols);

public sealed record StrategyPerformanceFigure(
    int Ordinal,
    string Label,
    string Value);

public sealed record StrategyEquityPoint(
    int Ordinal,
    string PeriodLabel,
    decimal Equity);

public sealed record StrategyAuthorView(
    string Name,
    string Initials,
    int StrategyCount,
    decimal RatingAverage);

public sealed record StrategyDetailView(
    StrategyCatalogItem Item,
    string Summary,
    string Description,
    StrategyAuthorView Author,
    IReadOnlyList<StrategyPerformanceFigure> Performance,
    IReadOnlyList<StrategyEquityPoint> EquityCurve,
    int ReviewCount);

public sealed record StrategyReviewView(
    Guid Id,
    string DisplayName,
    string Initials,
    int Rating,
    string Body,
    string Meta,
    DateTimeOffset CreatedAt);

public sealed record BotMetricView(
    string Window,
    decimal PlAmount,
    string Currency,
    int TradeCount);

public sealed record BotView(
    Guid Id,
    string Name,
    Guid StrategyId,
    string StrategyName,
    Guid? BrokerAccountId,
    string? MaskedLogin,
    string Symbol,
    string RiskLabel,
    BotStatus Status,
    BotHost Host,
    IReadOnlyList<BotMetricView> Metrics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateBot(
    Guid StrategyId,
    Guid? BrokerAccountId,
    string Name,
    string Symbol,
    string RiskLabel,
    BotHost Host);

public sealed record BotStatusChange(BotStatus Status);

/// <summary>One EA input value an operator moved off the strategy's declaration.</summary>
public sealed record BotInputValue(string Name, string Value);

/// <summary>
/// Everything a bot is configured with, in one read.
/// <paramref name="Declared"/> is the EA's own <c>input</c> parameters, read live
/// from the strategy the bot runs, in source order and with the members of every
/// enumeration it declares, so the settings form renders exactly what the
/// backtest dialog renders. <paramref name="Overrides"/> is only what the operator
/// actually changed: an input absent from it is running at the declared default,
/// and stays there when a corrected import changes that default.
/// <para>
/// All four settings are always populated. <paramref name="Symbol"/> is required
/// of every bot. <paramref name="Timeframe"/> is one of MetaTrader's twenty-one
/// chart periods — <c>M1 M2 M3 M4 M5 M6 M10 M12 M15 M20 M30 H1 H2 H3 H4 H6 H8
/// H12 D1 W1 MN1</c> — never a minute count and never a lowercase spelling, and
/// <paramref name="Volume"/> is always greater than zero. A bot whose operator
/// has never stated a timeframe or a volume stores neither, and the read reports
/// <c>H1</c> and <c>0.01</c> for it: the values the form starts from, not values
/// written to the row. <paramref name="MagicNumber"/> is <c>0</c> when unstated,
/// which is MetaTrader's own "no magic number".
/// </para>
/// </summary>
public sealed record BotSettingsView(
    Guid BotId,
    Guid StrategyId,
    string StrategyName,
    string Symbol,
    string Timeframe,
    decimal Volume,
    long MagicNumber,
    IReadOnlyList<StrategyInputView> Declared,
    IReadOnlyList<BotInputValue> Overrides);

/// <summary>
/// A complete replacement of a bot's settings. <paramref name="Timeframe"/> must
/// be one of MetaTrader's twenty-one chart periods and <paramref name="Volume"/>
/// must be a positive size with at most two decimal places, so what a later read
/// returns is always a period the platform names and a size a broker could accept.
/// <paramref name="Inputs"/> is the
/// whole intended set of values for the EA's declared inputs; every value equal
/// to the strategy's own declared default is discarded rather than stored, so
/// what is persisted stays exactly the set of overrides. An input the strategy
/// does not declare, a duplicate, or a value that does not parse for its declared
/// kind is refused; nothing is coerced.
/// </summary>
public sealed record UpdateBotSettings(
    string Symbol,
    string Timeframe,
    decimal Volume,
    long MagicNumber,
    IReadOnlyList<BotInputValue> Inputs);

/// <summary>
/// One instrument a broker server reports, as it reported it. Every field a
/// broker declines to report stays null rather than becoming a zero, so a caller
/// can tell an unreported minimum volume from a broker that really allows none.
/// </summary>
public sealed record BrokerSymbolView(
    string Server,
    string Symbol,
    string? Description,
    int? Digits,
    decimal? VolumeMin,
    decimal? VolumeMax,
    decimal? VolumeStep,
    string? Path);

public sealed record BotUptimeSample(
    int Ordinal,
    DateOnly SampledOn,
    decimal UptimeRatio,
    int DowntimeMinutes);

public sealed record BotUptimeProjection(
    int Days,
    int TotalDowntimeMinutes,
    IReadOnlyList<BotUptimeSample> Samples);

public sealed record BacktestView(
    Guid Id,
    Guid StrategyId,
    string StrategyName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal NetProfitAmount,
    decimal MaxDrawdownPercent,
    decimal ProfitFactor,
    int TradeCount,
    string Currency,
    BacktestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// One MQL5 <c>input</c> parameter a strategy declares, exactly as its source
/// writes it. <paramref name="Label"/> and <paramref name="GroupLabel"/> are the
/// trailing comment and the <c>input group</c> heading MetaTrader renders in its
/// properties dialog; both stay null when the source carries neither.
/// <paramref name="EnumMembers"/> is empty when the declared enumeration is not
/// itself declared in the strategy, in which case its members are genuinely
/// unknown and no submitted value for that input can be verified.
/// </summary>
public sealed record StrategyInputView(
    int Ordinal,
    string Name,
    string? Label,
    string? GroupLabel,
    string DeclaredType,
    string ValueKind,
    string DefaultValue,
    string? EnumTypeName,
    IReadOnlyList<StrategyEnumMemberView> EnumMembers,
    int SourceLine);

public sealed record StrategyEnumMemberView(
    int Ordinal,
    string Name,
    long Value,
    string? Label);

public sealed record StrategyInputsView(
    Guid StrategyId,
    string StrategyName,
    IReadOnlyList<StrategyInputView> Inputs);

public sealed record BacktestInputValue(string Name, string Value);

/// <summary>
/// One sample of a stored backtest equity curve.
/// <paramref name="Ordinal"/> is the position in the stored series and
/// <paramref name="SourceOrdinal"/> is the position in the untouched series the
/// run produced. They differ exactly when the series was thinned before it was
/// stored, which is what makes the thinning legible point by point rather than
/// only from the header on <see cref="BacktestEquityCurveView"/>.
/// </summary>
public sealed record BacktestEquityPoint(int Ordinal, int SourceOrdinal, decimal Equity);

/// <summary>
/// The equity curve a run measured, with everything needed to read it honestly.
/// <paramref name="SampleCount"/> is how many samples the run actually produced,
/// not how many were kept, and <paramref name="DecimationInterval"/> is the
/// stride that was stored: 1 means <paramref name="Points"/> is the whole
/// series, and k means every k-th sample was kept plus the final one.
/// <paramref name="InitialDeposit"/> is the balance the run started from, which
/// is the baseline the curve is read against.
/// </summary>
public sealed record BacktestEquityCurveView(
    decimal InitialDeposit,
    int SampleCount,
    int DecimationInterval,
    IReadOnlyList<BacktestEquityPoint> Points);

public sealed record CreateBacktest(
    Guid StrategyId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Symbol,
    string Timeframe,
    string Model,
    IReadOnlyList<BacktestInputValue> Inputs);

/// <summary>
/// A backtest request with the parameters it was submitted with.
/// <paramref name="DataQualityPercent"/> is null until a real measurement
/// exists; it is never defaulted to a number, and
/// <paramref name="DataQualitySource"/> names the artifact a present
/// measurement came from. <paramref name="EquityCurve"/> is null when the request
/// has not produced a curve — it has not run, it failed, or it completed before
/// curves were stored — and is never substituted with an empty or invented one.
/// </summary>
public sealed record BacktestDetailView(
    BacktestView Summary,
    string Symbol,
    string Timeframe,
    string Model,
    decimal? DataQualityPercent,
    string? DataQualitySource,
    string? FailureReason,
    IReadOnlyList<BacktestInputValue> Inputs,
    BacktestEquityCurveView? EquityCurve = null);

/// <summary>One rejected field of a submitted backtest input set.</summary>
public sealed record BacktestInputError(string Name, string Code, string Message);

/// <summary>
/// Raised when submitted backtest inputs do not match the strategy's declared
/// inputs. Carries one entry per offending field; nothing is ever coerced.
/// </summary>
public sealed class BacktestInputValidationException : Exception
{
    public BacktestInputValidationException(IReadOnlyList<BacktestInputError> errors)
        : base("The submitted backtest inputs were rejected.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    public BacktestInputValidationException()
        : this([])
    {
    }

    public BacktestInputValidationException(string message)
        : base(message) => Errors = [];

    public BacktestInputValidationException(string message, Exception innerException)
        : base(message, innerException) => Errors = [];

    public string Code { get; } = "BACKTEST_INPUTS_INVALID";

    public IReadOnlyList<BacktestInputError> Errors { get; }
}

public sealed record CloudPlanView(
    Guid Id,
    string Code,
    string Name,
    string? Tag,
    string Blurb,
    int PriceMonthlyCents,
    int PriceYearlyCents,
    string Currency,
    string Unit,
    string CtaLabel,
    bool Highlighted,
    IReadOnlyList<string> Features);

public sealed record CloudRegionView(
    string Code,
    string Label);

public sealed record CloudRunnerView(
    Guid Id,
    Guid BotId,
    string BotName,
    string RegionCode,
    string RegionLabel,
    decimal Uptime30dPercent,
    int LatencyMs,
    int MonthlyPriceCents,
    string Currency,
    CloudRunnerStatus Status,
    DateTimeOffset? NextInvoiceAt);

public sealed record JournalQuery(
    int Limit,
    Guid? Before,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record JournalEntryView(
    Guid Id,
    Guid? BotId,
    string? BotName,
    string Symbol,
    TradeSide Side,
    decimal Volume,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? ResultAmount,
    string Currency,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt);

public sealed record JournalPage(
    IReadOnlyList<JournalEntryView> Items,
    Guid? NextCursor);

public sealed record DashboardStatView(
    string Id,
    string Label,
    string Value,
    string Delta,
    TrendDirection Direction);

public sealed record DashboardSummaryView(
    IReadOnlyList<DashboardStatView> Stats,
    IReadOnlyList<BotView> RunningBots,
    int LiveBotCount,
    int CloudRunnerCount);

public sealed record BridgeStatusView(
    bool Connected,
    string Version,
    int RoundTripMs,
    int OrdersToday,
    int Rejections);

public interface IFrontendProjectionApplication
{
    Task<StrategyCatalogPage> GetStrategyCatalogAsync(
        UserActor actor,
        StrategyCatalogQuery query,
        CancellationToken cancellationToken);

    Task<StrategyDetailView?> GetStrategyDetailAsync(
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StrategyReviewView>> GetStrategyReviewsAsync(
        UserActor actor,
        Guid strategyId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BotView>> GetBotsAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<BotView?> GetBotAsync(
        UserActor actor,
        Guid botId,
        CancellationToken cancellationToken);

    Task<BotView> CreateBotAsync(
        UserActor actor,
        CreateBot request,
        CancellationToken cancellationToken);

    Task<BotView?> SetBotStatusAsync(
        UserActor actor,
        Guid botId,
        BotStatusChange request,
        CancellationToken cancellationToken);

    Task<BotUptimeProjection> GetBotUptimeAsync(
        UserActor actor,
        int days,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the bot's settings merged with its strategy's declared inputs, or
    /// null when the bot is not the caller's.
    /// </summary>
    Task<BotSettingsView?> GetBotSettingsAsync(
        UserActor actor,
        Guid botId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the bot's settings. Returns false when the bot is not the
    /// caller's, so the caller reports a missing resource rather than a fault.
    /// </summary>
    Task<bool> UpdateBotSettingsAsync(
        UserActor actor,
        Guid botId,
        UpdateBotSettings request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The instruments imported from a broker server, in symbol order. A null
    /// <paramref name="server"/> spans every imported server; a non-null
    /// <paramref name="query"/> narrows to symbols or descriptions containing it,
    /// case-insensitively.
    /// </summary>
    Task<IReadOnlyList<BrokerSymbolView>> GetBrokerSymbolsAsync(
        UserActor actor,
        string? server,
        string? query,
        CancellationToken cancellationToken);

    Task<StrategyInputsView?> GetStrategyInputsAsync(
        UserActor actor,
        Guid strategyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BacktestView>> GetBacktestsAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<BacktestDetailView?> GetBacktestDetailAsync(
        UserActor actor,
        Guid backtestId,
        CancellationToken cancellationToken);

    Task<BacktestView> CreateBacktestAsync(
        UserActor actor,
        CreateBacktest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudPlanView>> GetCloudPlansAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudRunnerView>> GetCloudRunnersAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudRegionView>> GetCloudRegionsAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<JournalPage> GetJournalAsync(
        UserActor actor,
        JournalQuery query,
        CancellationToken cancellationToken);

    Task<DashboardSummaryView> GetDashboardSummaryAsync(
        UserActor actor,
        CancellationToken cancellationToken);

    Task<BridgeStatusView> GetBridgeStatusAsync(
        UserActor actor,
        CancellationToken cancellationToken);
}
