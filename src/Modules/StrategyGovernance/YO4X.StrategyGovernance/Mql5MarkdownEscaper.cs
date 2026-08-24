using System.Globalization;
using System.Text;

namespace YO4X.StrategyGovernance;

public static class Mql5MarkdownEscaper
{
    public static string EscapeTableCell(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var escaped = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                escaped.Append(' ');
                continue;
            }

            string? entity = character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\\' => "&#92;",
                '|' => "&#124;",
                '`' => "&#96;",
                '[' => "&#91;",
                ']' => "&#93;",
                '(' => "&#40;",
                ')' => "&#41;",
                '*' => "&#42;",
                '_' => "&#95;",
                '~' => "&#126;",
                '#' => "&#35;",
                '!' => "&#33;",
                _ => null
            };
            if (entity is null)
            {
                escaped.Append(character);
            }
            else
            {
                escaped.Append(entity);
            }
        }

        return escaped.ToString();
    }
}
