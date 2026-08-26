using System.Collections.Frozen;
using System.Globalization;

namespace YO4X.Mql5.Runtime;

/// <summary>
/// The MQL5 <c>color</c> type and the named web colours <c>ColorToString</c> and
/// <c>StringToColor</c> recognise.
///
/// MQL5 stores a colour as <c>0x00BBGGRR</c> - blue in the high byte - which is the
/// reverse of the <c>0xRRGGBB</c> byte order every other system uses. Getting that
/// backwards silently swaps red and blue in every drawn object, so the packing lives
/// in one place here rather than being open-coded at each call site.
///
/// The name table carries the standard web colours. A name outside it is not an
/// error: <c>ColorToString</c> falls back to MQL5's numeric <c>"R,G,B"</c> form, which
/// is what MQL5 itself emits for an unnamed colour.
/// </summary>
public static class Mql5Colors
{
    private static readonly (string Name, int Rgb)[] Table =
    [
        ("Black", 0x000000), ("White", 0xFFFFFF), ("Red", 0xFF0000), ("Green", 0x008000),
        ("Blue", 0x0000FF), ("Yellow", 0xFFFF00), ("Magenta", 0xFF00FF), ("Aqua", 0x00FFFF),
        ("Lime", 0x00FF00), ("Maroon", 0x800000), ("Navy", 0x000080), ("Olive", 0x808000),
        ("Purple", 0x800080), ("Teal", 0x008080), ("Gray", 0x808080), ("Silver", 0xC0C0C0),
        ("DarkGreen", 0x006400), ("Orange", 0xFFA500), ("Pink", 0xFFC0CB), ("Gold", 0xFFD700),
        ("Brown", 0xA52A2A), ("Crimson", 0xDC143C), ("DodgerBlue", 0x1E90FF), ("DeepSkyBlue", 0x00BFFF),
        ("LimeGreen", 0x32CD32), ("ForestGreen", 0x228B22), ("SteelBlue", 0x4682B4), ("Tomato", 0xFF6347),
        ("Indigo", 0x4B0082), ("Khaki", 0xF0E68C), ("Lavender", 0xE6E6FA), ("Salmon", 0xFA8072),
        ("SeaGreen", 0x2E8B57), ("SkyBlue", 0x87CEEB), ("SlateBlue", 0x6A5ACD), ("Violet", 0xEE82EE),
        ("Wheat", 0xF5DEB3), ("Chocolate", 0xD2691E), ("CornflowerBlue", 0x6495ED), ("DarkBlue", 0x00008B),
        ("DarkRed", 0x8B0000), ("LightBlue", 0xADD8E6), ("LightGray", 0xD3D3D3), ("MediumBlue", 0x0000CD),
        ("Orchid", 0xDA70D6), ("RoyalBlue", 0x4169E1), ("Sienna", 0xA0522D), ("Turquoise", 0x40E0D0)
    ];

    private static readonly FrozenDictionary<string, int> ByName = Table
        .ToFrozenDictionary(entry => entry.Name, entry => FromRgb(entry.Rgb), StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<int, string> ByValue = Table
        .GroupBy(entry => FromRgb(entry.Rgb))
        .ToFrozenDictionary(group => group.Key, group => group.First().Name);

    /// <summary>Packs red, green and blue into MQL5's <c>0x00BBGGRR</c> layout.</summary>
    public static int Pack(int red, int green, int blue)
        => (red & 0xFF) | ((green & 0xFF) << 8) | ((blue & 0xFF) << 16);

    /// <summary>Unpacks an MQL5 colour into its red, green and blue components.</summary>
    public static (int Red, int Green, int Blue) Unpack(int color)
        => (color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF);

    /// <summary>Converts a conventional <c>0xRRGGBB</c> value to MQL5's layout.</summary>
    public static int FromRgb(int rgb) => Pack((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>
    /// The MQL5 name of <paramref name="color"/> without its <c>clr</c> prefix, or null
    /// when the colour has no standard name.
    /// </summary>
    public static string? Name(int color) => ByValue.TryGetValue(color & 0xFFFFFF, out string? found) ? found : null;

    /// <summary>
    /// Resolves a colour written as a name (<c>clrRed</c> or <c>Red</c>) or as MQL5's
    /// <c>"R,G,B"</c> triple. Returns false for anything else, which is how
    /// <c>StringToColor</c> reports a failure.
    /// </summary>
    public static bool TryParse(string? text, out int color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith("clr", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 3)
        {
            string bare = trimmed[3..];
            if (ByName.TryGetValue(bare, out int named))
            {
                color = named;
                return true;
            }
        }

        if (ByName.TryGetValue(trimmed, out int direct))
        {
            color = direct;
            return true;
        }

        string[] parts = trimmed.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int red)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int green)
            || !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int blue))
        {
            return false;
        }

        color = Pack(red, green, blue);
        return true;
    }
}
