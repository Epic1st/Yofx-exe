using System.Globalization;

namespace YO4X.Strategy.Abstractions;

/// <summary>
/// Defines the textual invariant used by canonical strategy evidence.
/// Values must have an unambiguous Unicode-scalar representation and must not
/// hide boundary whitespace or control/format characters in durable identity
/// fields.
/// </summary>
public static class StrategyCanonicalText
{
    public static bool IsCanonical(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        for (int index = 0; index < value.Length;)
        {
            char current = value[index];
            int scalarWidth;
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                scalarWidth = 2;
            }
            else if (char.IsLowSurrogate(current))
            {
                return false;
            }
            else
            {
                scalarWidth = 1;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                return false;
            }

            index += scalarWidth;
        }

        return true;
    }
}
