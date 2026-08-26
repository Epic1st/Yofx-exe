using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.StrategyInputProjection;

/// <summary>
/// The deterministic identifiers the catalog projection already uses.
///
/// A strategy row's identifier is derived from the corpus digest and the file's
/// relative path exactly as scripts/build-catalog-sql.mjs derives it: the first
/// sixteen bytes of SHA-256 over "corpusSha256 relativePath", with the version
/// nibble forced to 7 and the variant nibble folded into 8, 9, a or b. Deriving
/// it any other way would orphan every row this tool writes, so this is a
/// deliberate, byte-for-byte restatement of that script rather than an
/// independent scheme.
/// </summary>
internal static class StrategyProjectionIdentity
{
    private const string VariantNibbles = "89ab";

    /// <summary>The identifier of the catalog strategy row for one corpus file.</summary>
    public static Guid ForStrategy(string corpusSha256, string relativePath) =>
        Derive(corpusSha256 + " " + relativePath);

    /// <summary>A stable identifier for one projected input row.</summary>
    public static Guid ForInput(string corpusSha256, string relativePath, int ordinal) =>
        Derive(
            corpusSha256
            + " "
            + relativePath
            + " input "
            + ordinal.ToString(CultureInfo.InvariantCulture));

    /// <summary>A stable identifier for one projected enumeration member row.</summary>
    public static Guid ForEnumMember(
        string corpusSha256,
        string relativePath,
        string enumTypeName,
        int ordinal) =>
        Derive(
            corpusSha256
            + " "
            + relativePath
            + " enum "
            + enumTypeName
            + " "
            + ordinal.ToString(CultureInfo.InvariantCulture));

    private static Guid Derive(string material)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(material);
        char[] hex;
        try
        {
            hex = Convert.ToHexString(SHA256.HashData(bytes))
                .ToLowerInvariant()
                .AsSpan(0, 32)
                .ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        hex[12] = '7';
        hex[16] = VariantNibbles[
            int.Parse(hex[16].ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture) % 4];
        var value = new string(hex);
        return Guid.ParseExact(
            value[..8]
                + "-"
                + value[8..12]
                + "-"
                + value[12..16]
                + "-"
                + value[16..20]
                + "-"
                + value[20..],
            "D");
    }
}
