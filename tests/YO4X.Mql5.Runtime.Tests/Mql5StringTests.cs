using YO4X.Mql5.Runtime;

namespace YO4X.Mql5.Runtime.Tests;

/// <summary>
/// The <c>String*</c> family, including the shapes MQL5 gives them that C# would not:
/// mutators that take the subject by reference and return a count or a flag rather than
/// the new string.
/// </summary>
public sealed class Mql5StringTests
{
    private static Mql5Runtime Build() => new(new FakeMarketContext());

    [Fact]
    public void StringLenCountsCharactersAndTreatsNullAsEmpty()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal(5, runtime.StringLen("hello"));
        Assert.Equal(0, runtime.StringLen(string.Empty));
        Assert.Equal(0, runtime.StringLen(null));
    }

    [Theory]
    [InlineData("hello world", 6, -1, "world")]
    [InlineData("hello world", 0, 5, "hello")]
    [InlineData("hello", 10, -1, "")]
    [InlineData("hello", -1, 2, "")]
    [InlineData("hello", 3, 99, "lo")]
    [InlineData("hello", 2, 0, "")]
    public void StringSubstrClampsRatherThanThrowing(string value, int start, int length, string expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringSubstr(value, start, length));
    }

    [Theory]
    [InlineData("hello world", "world", 0, 6)]
    [InlineData("hello world", "xyz", 0, -1)]
    [InlineData("aXbXc", "X", 2, 3)]
    [InlineData("abc", "abc", 0, 0)]
    public void StringFindReturnsMinusOneOnAMiss(string value, string needle, int start, int expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringFind(value, needle, start));
    }

    [Fact]
    public void StringFindOnNullIsAMissRatherThanAThrow()
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(-1, runtime.StringFind(null, "x"));
    }

    [Fact]
    public void StringReplaceEditsInPlaceAndCountsReplacements()
    {
        Mql5Runtime runtime = Build();
        string subject = "a-b-c";

        Assert.Equal(2, runtime.StringReplace(ref subject, "-", "+"));
        Assert.Equal("a+b+c", subject);

        Assert.Equal(0, runtime.StringReplace(ref subject, "z", "!"));
        Assert.Equal("a+b+c", subject);
    }

    [Fact]
    public void StringReplaceWithAnEmptyNeedleIsAnError()
    {
        Mql5Runtime runtime = Build();
        string subject = "abc";

        Assert.Equal(-1, runtime.StringReplace(ref subject, string.Empty, "x"));
        Assert.Equal("abc", subject);
    }

    [Fact]
    public void StringSplitFillsTheTargetArray()
    {
        Mql5Runtime runtime = Build();
        string[] parts = [];

        Assert.Equal(3, runtime.StringSplit("a,b,c", ',', ref parts));
        Assert.Equal(["a", "b", "c"], parts);

        Assert.Equal(0, runtime.StringSplit(string.Empty, ',', ref parts));
        Assert.Empty(parts);
    }

    [Fact]
    public void StringTrimReportsHowManyCharactersItRemoved()
    {
        Mql5Runtime runtime = Build();

        string left = "   abc";
        Assert.Equal(3, runtime.StringTrimLeft(ref left));
        Assert.Equal("abc", left);

        string right = "abc\t\n";
        Assert.Equal(2, runtime.StringTrimRight(ref right));
        Assert.Equal("abc", right);
    }

    [Fact]
    public void CaseConversionEditsInPlaceAndReturnsSuccess()
    {
        Mql5Runtime runtime = Build();

        string value = "MiXeD";
        Assert.True(runtime.StringToUpper(ref value));
        Assert.Equal("MIXED", value);

        Assert.True(runtime.StringToLower(ref value));
        Assert.Equal("mixed", value);
    }

    [Fact]
    public void StringAddAppendsInPlace()
    {
        Mql5Runtime runtime = Build();
        string value = "abc";

        Assert.True(runtime.StringAdd(ref value, "def"));
        Assert.Equal("abcdef", value);
    }

    [Fact]
    public void StringConcatenateWritesThroughAndReturnsTheLength()
    {
        Mql5Runtime runtime = Build();
        string target = string.Empty;

        Assert.Equal(10, runtime.StringConcatenate(ref target, "EURUSD", " ", 0.1));
        Assert.Equal("EURUSD 0.1", target);
    }

    [Theory]
    [InlineData("abc", "abd", true, -1)]
    [InlineData("abd", "abc", true, 1)]
    [InlineData("abc", "abc", true, 0)]
    [InlineData("ABC", "abc", false, 0)]
    [InlineData("ABC", "abc", true, -1)]
    public void StringCompareReportsTheSign(string first, string second, bool caseSensitive, int expected)
    {
        Mql5Runtime runtime = Build();
        Assert.Equal(expected, runtime.StringCompare(first, second, caseSensitive));
    }

    [Fact]
    public void StringGetCharacterAnswersZeroOutsideTheString()
    {
        Mql5Runtime runtime = Build();

        Assert.Equal('b', (char)runtime.StringGetCharacter("abc", 1));
        Assert.Equal(0, runtime.StringGetCharacter("abc", 9));
        Assert.Equal(Mql5ErrorCodes.StringSmallLength, runtime.GetLastError());
    }

    [Fact]
    public void StringSetCharacterReplacesAppendsAndTruncates()
    {
        Mql5Runtime runtime = Build();

        string value = "abc";
        Assert.True(runtime.StringSetCharacter(ref value, 1, 'X'));
        Assert.Equal("aXc", value);

        Assert.True(runtime.StringSetCharacter(ref value, 3, 'D'));
        Assert.Equal("aXcD", value);

        Assert.True(runtime.StringSetCharacter(ref value, 2, 0));
        Assert.Equal("aX", value);

        Assert.False(runtime.StringSetCharacter(ref value, 99, 'Z'));
    }

    [Fact]
    public void StringInitAndStringFillBuildRepeatedCharacters()
    {
        Mql5Runtime runtime = Build();

        string value = string.Empty;
        Assert.True(runtime.StringInit(ref value, 4, '='));
        Assert.Equal("====", value);

        Assert.True(runtime.StringFill(ref value, '-'));
        Assert.Equal("----", value);

        Assert.True(runtime.StringInit(ref value, 4, 0));
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void StringBufferLenAndReserveAreHonestAboutImmutableStrings()
    {
        Mql5Runtime runtime = Build();

        string value = "abc";
        Assert.Equal(3, runtime.StringBufferLen(value));
        Assert.True(runtime.StringReserve(ref value, 100));
        Assert.Equal("abc", value);
    }
}
