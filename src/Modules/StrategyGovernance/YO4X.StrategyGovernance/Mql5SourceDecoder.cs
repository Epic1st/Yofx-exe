using System.Text;

namespace YO4X.StrategyGovernance;

internal enum Mql5SourceContentKind
{
    Text,
    AllNul,
    Binary
}

internal sealed record Mql5DecodedSource(
    string Text,
    string EncodingName,
    bool UsedFallbackEncoding,
    Mql5SourceContentKind ContentKind,
    int ForbiddenControlCharacterCount);

internal static class Mql5SourceDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(
        bigEndian: true,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    public static Mql5DecodedSource Decode(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Encoding.UTF8.Preamble))
        {
            return DecodeStrictOrBinary(
                content,
                content[Encoding.UTF8.Preamble.Length..],
                StrictUtf8,
                "utf-8-bom",
                usedFallbackEncoding: false);
        }

        if (content.StartsWith(Encoding.Unicode.Preamble))
        {
            return DecodeStrictOrBinary(
                content,
                content[Encoding.Unicode.Preamble.Length..],
                StrictUtf16LittleEndian,
                "utf-16le",
                usedFallbackEncoding: false);
        }

        if (content.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return DecodeStrictOrBinary(
                content,
                content[Encoding.BigEndianUnicode.Preamble.Length..],
                StrictUtf16BigEndian,
                "utf-16be",
                usedFallbackEncoding: false);
        }

        if (!content.IsEmpty && IsAllNul(content))
        {
            return new Mql5DecodedSource(
                Encoding.Latin1.GetString(content),
                "binary-all-nul",
                true,
                Mql5SourceContentKind.AllNul,
                0);
        }

        UnicodeEncoding? bomlessUtf16 = DetectBomlessUtf16(content);
        if (bomlessUtf16 is not null)
        {
            return DecodeStrictOrBinary(
                content,
                content,
                bomlessUtf16,
                bomlessUtf16.CodePage == Encoding.Unicode.CodePage
                    ? "utf-16le-no-bom"
                    : "utf-16be-no-bom",
                usedFallbackEncoding: false);
        }

        if (content.Contains((byte)0))
        {
            return CreateBinary(content);
        }

        try
        {
            return CreateText(
                StrictUtf8.GetString(content),
                "utf-8",
                usedFallbackEncoding: false);
        }
        catch (DecoderFallbackException)
        {
            if (TryDecodeWindows1252(content, out string? windows1252))
            {
                return CreateText(
                    windows1252!,
                    "windows-1252",
                    usedFallbackEncoding: true);
            }

            return CreateBinary(content);
        }
    }

    private static Mql5DecodedSource DecodeStrictOrBinary(
        ReadOnlySpan<byte> originalContent,
        ReadOnlySpan<byte> encodedText,
        Encoding encoding,
        string encodingName,
        bool usedFallbackEncoding)
    {
        try
        {
            return CreateText(
                encoding.GetString(encodedText),
                encodingName,
                usedFallbackEncoding);
        }
        catch (DecoderFallbackException)
        {
            return CreateBinary(originalContent);
        }
    }

    private static Mql5DecodedSource CreateBinary(ReadOnlySpan<byte> content)
    {
        string bytePreservingText = Encoding.Latin1.GetString(content);
        return new Mql5DecodedSource(
            bytePreservingText,
            "binary-non-text",
            true,
            Mql5SourceContentKind.Binary,
            CountForbiddenControls(bytePreservingText));
    }

    private static Mql5DecodedSource CreateText(
        string text,
        string encodingName,
        bool usedFallbackEncoding) => new(
            text,
            encodingName,
            usedFallbackEncoding,
            Mql5SourceContentKind.Text,
            CountForbiddenControls(text));

    private static int CountForbiddenControls(string text)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (IsForbiddenControl(character))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsForbiddenControl(char character) =>
        (character < ' ' && character is not ('\t' or '\n' or '\r'))
        || character == '\u007f';

    private static bool TryDecodeWindows1252(
        ReadOnlySpan<byte> content,
        out string? decoded)
    {
        var characters = new char[content.Length];
        for (int index = 0; index < content.Length; index++)
        {
            byte value = content[index];
            if (value < 0x80 || value >= 0xa0)
            {
                characters[index] = (char)value;
                continue;
            }

            char mapped = value switch
            {
                0x80 => '\u20ac',
                0x82 => '\u201a',
                0x83 => '\u0192',
                0x84 => '\u201e',
                0x85 => '\u2026',
                0x86 => '\u2020',
                0x87 => '\u2021',
                0x88 => '\u02c6',
                0x89 => '\u2030',
                0x8a => '\u0160',
                0x8b => '\u2039',
                0x8c => '\u0152',
                0x8e => '\u017d',
                0x91 => '\u2018',
                0x92 => '\u2019',
                0x93 => '\u201c',
                0x94 => '\u201d',
                0x95 => '\u2022',
                0x96 => '\u2013',
                0x97 => '\u2014',
                0x98 => '\u02dc',
                0x99 => '\u2122',
                0x9a => '\u0161',
                0x9b => '\u203a',
                0x9c => '\u0153',
                0x9e => '\u017e',
                0x9f => '\u0178',
                _ => '\0'
            };
            if (mapped == '\0')
            {
                decoded = null;
                return false;
            }

            characters[index] = mapped;
        }

        decoded = new string(characters);
        return true;
    }

    private static bool IsAllNul(ReadOnlySpan<byte> content)
    {
        foreach (byte value in content)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static UnicodeEncoding? DetectBomlessUtf16(ReadOnlySpan<byte> content)
    {
        if (content.Length < 64 || content.Length % 2 != 0)
        {
            return null;
        }

        int sampleLength = Math.Min(content.Length, 4096) & ~1;
        int pairCount = sampleLength / 2;
        int evenNulCount = 0;
        int oddNulCount = 0;
        for (int index = 0; index < sampleLength; index += 2)
        {
            evenNulCount += content[index] == 0 ? 1 : 0;
            oddNulCount += content[index + 1] == 0 ? 1 : 0;
        }

        int minimumExpectedNuls = pairCount * 3 / 10;
        int maximumOppositeNuls = pairCount / 20;
        if (oddNulCount >= minimumExpectedNuls && evenNulCount <= maximumOppositeNuls)
        {
            return StrictUtf16LittleEndian;
        }

        if (evenNulCount >= minimumExpectedNuls && oddNulCount <= maximumOppositeNuls)
        {
            return StrictUtf16BigEndian;
        }

        return null;
    }
}
