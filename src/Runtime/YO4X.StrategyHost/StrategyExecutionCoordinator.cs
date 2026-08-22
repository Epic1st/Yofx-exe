using System.Diagnostics;
using YO4X.Strategy.Abstractions;

namespace YO4X.StrategyHost;

public sealed class StrategyExecutionCoordinator
{
    public StrategyResultValidation Execute(
        IYo4xStrategy strategy,
        StrategyEvent input,
        StrategySnapshot snapshot,
        StrategyState currentState,
        StrategyResultBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(bounds);

        long startedAt = Stopwatch.GetTimestamp();
        StrategyResult result = strategy.Handle(input, snapshot, currentState);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        return StrategyResultValidator.Validate(currentState, result, bounds, elapsed);
    }
}
