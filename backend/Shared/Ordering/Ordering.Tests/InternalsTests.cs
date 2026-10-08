namespace Ordering.Tests;

/// <summary>
///     Tests for internal helpers exposed via InternalsVisibleTo.
///     Cover each algorithmic piece in isolation.
/// </summary>
public class InternalsTests
{
    // ─── DigitTable ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('A', 10)]
    [InlineData('Z', 35)]
    [InlineData('a', 36)]
    [InlineData('z', 61)]
    public void DigitValue_ValidBase62Chars_ReturnsExpectedIndex(char c, int expected)
    {
        Assert.Equal(expected, FractionalIndexing.DigitValue(c));
    }

    [Theory]
    [InlineData('!')]
    [InlineData('@')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData('`')]
    [InlineData('{')]
    [InlineData(' ')]
    [InlineData('\0')]
    public void DigitValue_InvalidChars_Throws(char c)
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.DigitValue(c));
    }

    [Fact]
    public void DigitValue_NonAsciiChar_Throws()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.DigitValue((char)200));
        Assert.Throws<ArgumentException>(() => FractionalIndexing.DigitValue('ё'));
    }

    [Fact]
    public void TryDigit_InvalidChar_ReturnsMinusOne()
    {
        Assert.Equal(-1, FractionalIndexing.TryDigit('!'));
        Assert.Equal(-1, FractionalIndexing.TryDigit((char)200));
    }

    [Fact]
    public void DigitTable_FullCoverage_AllSixtyTwoBaseDigitsMapBackToSameChar()
    {
        for (int i = 0; i < 62; i++)
        {
            char c = FractionalIndexing.DigitChar(i);
            Assert.Equal(i, FractionalIndexing.DigitValue(c));
        }
    }

    // ─── IntegerLength ────────────────────────────────────────────────────────

    [Theory]
    [InlineData('a', 2)]
    [InlineData('b', 3)]
    [InlineData('z', 27)]
    [InlineData('Z', 2)]
    [InlineData('Y', 3)]
    [InlineData('A', 27)]
    public void IntegerLength_ValidHeads_ReturnsExpected(char head, int expected)
    {
        Assert.Equal(expected, FractionalIndexing.IntegerLength(head));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('5')]
    [InlineData('!')]
    public void IntegerLength_InvalidHead_Throws(char head)
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.IntegerLength(head));
    }

    [Fact]
    public void TryIntegerLength_InvalidHead_ReturnsMinusOne()
    {
        Assert.Equal(-1, FractionalIndexing.TryIntegerLength('0'));
        Assert.Equal(-1, FractionalIndexing.TryIntegerLength('!'));
    }

    // ─── IncrementInteger ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("a0", "a1")]
    [InlineData("a1", "a2")]
    [InlineData("a9", "aA")]
    [InlineData("aZ", "aa")]
    [InlineData("ay", "az")]
    [InlineData("Zy", "Zz")]
    [InlineData("Zz", "a0")]                           // uppercase rollover → lowercase
    [InlineData("az", "b00")]                          // lowercase grow on overflow
    [InlineData("Yzz", "Z0")]                          // uppercase shrink on overflow
    [InlineData("Y00", "Y01")]
    public void IncrementInteger_KnownVectors_Match(string input, string expected)
    {
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.IncrementInteger(input.AsSpan(), buf);
        Assert.True(written >= 0);
        Assert.Equal(expected, buf[..written].ToString());
    }

    [Fact]
    public void IncrementInteger_AllLowercaseMax_ReturnsMinusOne()
    {
        // "z" has integer length 27 → "z" + 26 'z' = the largest representable integer.
        string max = "z" + new string('z', 26);
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.IncrementInteger(max.AsSpan(), buf);
        Assert.Equal(-1, written);
    }

    [Fact]
    public void IncrementInteger_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(static () =>
        {
            Span<char> b = stackalloc char[64];
            // 'a' integer must be length 2, give it length 3
            FractionalIndexing.IncrementInteger("a00".AsSpan(), b);
        });
    }

    // ─── DecrementInteger ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("a1", "a0")]
    [InlineData("a0", "Zz")]                           // lowercase rollover → uppercase
    [InlineData("Zz", "Zy")]
    [InlineData("Z0", "Yzz")]                          // uppercase grow on underflow
    [InlineData("b00", "az")]                          // lowercase shrink on underflow
    [InlineData("aa", "aZ")]
    public void DecrementInteger_KnownVectors_Match(string input, string expected)
    {
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.DecrementInteger(input.AsSpan(), buf);
        Assert.True(written >= 0);
        Assert.Equal(expected, buf[..written].ToString());
    }

    [Fact]
    public void DecrementInteger_SmallestInteger_ReturnsMinusOne()
    {
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.DecrementInteger(FractionalIndexing.SmallestInteger.AsSpan(), buf);
        Assert.Equal(-1, written);
    }

    // ─── Increment / decrement round-trip ────────────────────────────────────

    [Theory]
    [InlineData("a0")]
    [InlineData("a5")]
    [InlineData("az")]
    [InlineData("Zz")]
    [InlineData("Y00")]
    [InlineData("b00")]
    public void IncrementDecrement_RoundTrip(string original)
    {
        Span<char> incBuf = stackalloc char[64];
        int incLen = FractionalIndexing.IncrementInteger(original.AsSpan(), incBuf);
        Assert.True(incLen >= 0, $"Failed to increment {original}");

        Span<char> decBuf = stackalloc char[64];
        int decLen = FractionalIndexing.DecrementInteger(incBuf[..incLen], decBuf);
        Assert.True(decLen >= 0, $"Failed to decrement {incBuf[..incLen].ToString()}");

        Assert.Equal(original, decBuf[..decLen].ToString());
    }

    // ─── Midpoint ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "V", "G")]       // sum=31 odd → round half up
    [InlineData("", "1", "0V")]      // consecutive digits 0..1 → recurse
    [InlineData("0", "1", "0V")]
    [InlineData("", "G", "8")]
    public void Midpoint_KnownVectors_Match(string a, string b, string expected)
    {
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.Midpoint(a.AsSpan(), b.AsSpan(), hasB: true, buf);
        Assert.Equal(expected, buf[..written].ToString());
    }

    [Theory]
    [InlineData("", "V")]               // half-way digit in unbounded range
    [InlineData("V", "l")]
    public void Midpoint_NoUpperBound_EmitsMidDigit(string a, string expected)
    {
        Span<char> buf = stackalloc char[64];
        int written = FractionalIndexing.Midpoint(a.AsSpan(), default, hasB: false, buf);
        Assert.Equal(expected, buf[..written].ToString());
    }

    [Fact]
    public void Midpoint_AEqualsOrGreaterThanB_Throws()
    {
        Assert.Throws<ArgumentException>(static () =>
        {
            Span<char> buf = stackalloc char[64];
            FractionalIndexing.Midpoint("V".AsSpan(), "V".AsSpan(), hasB: true, buf);
        });

        Assert.Throws<ArgumentException>(static () =>
        {
            Span<char> buf = stackalloc char[64];
            FractionalIndexing.Midpoint("z".AsSpan(), "0".AsSpan(), hasB: true, buf);
        });
    }

    // ─── ValidateOrderKey ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("a0")]
    [InlineData("Zz")]
    [InlineData("a0V")]
    [InlineData("b125")]
    public void ValidateOrderKey_ValidKeys_DoesNotThrow(string key)
    {
        FractionalIndexing.ValidateOrderKey(key.AsSpan());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("a")]               // too short
    [InlineData("a@")]              // invalid char
    [InlineData("a10")]             // fractional ends with zero
    public void ValidateOrderKey_InvalidKeys_Throws(string key)
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.ValidateOrderKey(key.AsSpan()));
    }

    [Fact]
    public void ValidateOrderKey_SmallestInteger_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            FractionalIndexing.ValidateOrderKey(FractionalIndexing.SmallestInteger.AsSpan()));
    }
}
