using System.Globalization;

namespace YO4X.MarketData.Mt5Import;

/// <summary>
/// Deterministic <c>--option value</c> parsing. Repeated options, missing values and
/// values that look like another option all fail closed.
/// </summary>
internal static class Mt5CommandLine
{
    internal static bool HasSwitch(IReadOnlyList<string> arguments, string option)
    {
        int count = 0;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentException($"Option '{option}' can be specified only once.")
        };
    }

    internal static string GetRequiredOption(IReadOnlyList<string> arguments, string option) =>
        GetOptionalOption(arguments, option)
        ?? throw new ArgumentException($"Required option '{option}' is missing.");

    internal static string? GetOptionalOption(IReadOnlyList<string> arguments, string option)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (!arguments[candidate].Equals(option, StringComparison.Ordinal))
            {
                continue;
            }

            if (index >= 0)
            {
                throw new ArgumentException($"Option '{option}' can be specified only once.");
            }

            index = candidate;
        }

        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= arguments.Count
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Option '{option}' has no value.");
        }

        return arguments[index + 1];
    }

    internal static int GetOptionalCount(
        IReadOnlyList<string> arguments,
        string option,
        int fallback,
        int minimum,
        int maximum)
    {
        string? text = GetOptionalOption(arguments, option);
        if (text is null)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < minimum
            || value > maximum)
        {
            throw new ArgumentException(
                $"Option '{option}' must be a whole number between {minimum} and {maximum}.");
        }

        return value;
    }

    /// <summary>
    /// Parses a broker-server offset of the form <c>+HH:MM</c> or <c>-HH:MM</c>. There is no
    /// default: the offset is a property of the broker's server and must be stated by the caller.
    /// </summary>
    internal static TimeSpan ParseServerUtcOffset(string text)
    {
        if (text.Length != 6
            || (text[0] != '+' && text[0] != '-')
            || text[3] != ':')
        {
            throw new ArgumentException(
                "Option '--server-utc-offset' must be written as +HH:MM or -HH:MM.");
        }

        if (!int.TryParse(text.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || !int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || minutes > 59)
        {
            throw new ArgumentException(
                "Option '--server-utc-offset' must be written as +HH:MM or -HH:MM.");
        }

        var magnitude = new TimeSpan(hours, minutes, 0);
        if (magnitude > TimeSpan.FromHours(14))
        {
            throw new ArgumentException(
                "Option '--server-utc-offset' must lie between -14:00 and +14:00.");
        }

        return text[0] == '-' ? magnitude.Negate() : magnitude;
    }

    /// <summary>Parses a point in the trading week written as <c>Sunday:22:00</c>.</summary>
    internal static Mt5WeekInstant ParseWeekInstant(string option, string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length != 3
            || !Enum.TryParse(parts[0], ignoreCase: true, out DayOfWeek day)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || hours > 23
            || minutes > 59)
        {
            throw new ArgumentException(
                $"Option '{option}' must be written as DayOfWeek:HH:MM, for example Sunday:22:00.");
        }

        return new Mt5WeekInstant(day, new TimeSpan(hours, minutes, 0));
    }
}
