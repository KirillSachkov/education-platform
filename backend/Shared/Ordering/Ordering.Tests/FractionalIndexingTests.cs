namespace Ordering.Tests;

public class FractionalIndexingTests
{
    // ─── Sanity ───────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateKeyBetween_BothNull_ReturnsInitialKey()
    {
        Assert.Equal("a0", FractionalIndexing.GenerateKeyBetween(null, null));
    }

    [Fact]
    public void GenerateKeyBetween_AfterA0_ReturnsA1()
    {
        Assert.Equal("a1", FractionalIndexing.GenerateKeyBetween("a0", null));
    }

    [Fact]
    public void GenerateKeyBetween_BeforeA0_ReturnsZz()
    {
        Assert.Equal("Zz", FractionalIndexing.GenerateKeyBetween(null, "a0"));
    }

    [Fact]
    public void GenerateKeyBetween_BetweenA0AndA1_ReturnsMidpoint()
    {
        Assert.Equal("a0V", FractionalIndexing.GenerateKeyBetween("a0", "a1"));
    }

    [Fact]
    public void GenerateKeyBetween_BetweenA0AndA0V_ReturnsMidpoint()
    {
        Assert.Equal("a0G", FractionalIndexing.GenerateKeyBetween("a0", "a0V"));
    }

    // ─── Parameterized reference vectors ──────────────────────────────────────

    // Vectors derived from the rocicorp/fractional-indexing reference implementation
    // (CC0). Round-half-up applied to midpoint; same as the JS reference.
    [Theory]
    [InlineData(null, null, "a0")]
    [InlineData(null, "a0", "Zz")]
    [InlineData(null, "Zz", "Zy")]
    [InlineData("a0", null, "a1")]
    [InlineData("a1", null, "a2")]
    [InlineData("a0", "a1", "a0V")]
    [InlineData("a1", "a2", "a1V")]
    [InlineData("a0V", "a1", "a0l")]
    [InlineData("Zz", "a0", "ZzV")]
    [InlineData("Zz", "a01", "a0")]
    [InlineData("a0", "a0V", "a0G")]
    [InlineData("a0", "a0G", "a08")]
    [InlineData("b125", "b129", "b127")]
    [InlineData("a0", "a1V", "a1")]
    [InlineData(null, "a0V", "a0")]
    [InlineData(null, "b999", "b99")]
    public void GenerateKeyBetween_ReferenceVector_Matches(string? a, string? b, string expected)
    {
        Assert.Equal(expected, FractionalIndexing.GenerateKeyBetween(a, b));
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void GenerateKeyBetween_ThrowsWhenAGreaterThanOrEqualToB()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.GenerateKeyBetween("a1", "a0"));
        Assert.Throws<ArgumentException>(() => FractionalIndexing.GenerateKeyBetween("a0", "a0"));
    }

    [Theory]
    [InlineData("a")]    // too short
    [InlineData("0")]    // invalid head
    [InlineData("!")]    // invalid head
    [InlineData("a@")]   // invalid digit char
    [InlineData("a10")]  // fractional ends with zero
    public void GenerateKeyBetween_RejectsMalformedKey(string bad)
    {
        Assert.Throws<ArgumentException>(() => FractionalIndexing.GenerateKeyBetween(bad, null));
    }

    [Fact]
    public void GenerateNKeysBetween_RejectsNegativeN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FractionalIndexing.GenerateNKeysBetween(null, null, -1));
    }

    // ─── IsValidOrderKey ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("a0", true)]
    [InlineData("a1", true)]
    [InlineData("a0V", true)]
    [InlineData("Zz", true)]
    [InlineData("b125", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("a10", false)]     // frac ends with zero
    [InlineData("a", false)]        // too short
    [InlineData("0", false)]        // invalid head
    [InlineData("a@", false)]       // invalid digit
    public void IsValidOrderKey_Behaves(string? key, bool expected)
    {
        Assert.Equal(expected, FractionalIndexing.IsValidOrderKey(key));
    }

    [Fact]
    public void IsValidOrderKey_SmallestIntegerSentinel_ReturnsFalse()
    {
        // "A" + 26 zeros — exact sentinel value, never a valid key.
        string sentinel = "A" + new string('0', 26);
        Assert.False(FractionalIndexing.IsValidOrderKey(sentinel));
    }

    // ─── Property tests: ordering invariants ──────────────────────────────────

    [Fact]
    public void GenerateKeyBetween_1000SequentialAppends_AllSortedCorrectly()
    {
        List<string> keys = new(1000);
        string? prev = null;

        for (int i = 0; i < 1000; i++)
        {
            string key = FractionalIndexing.GenerateKeyBetween(prev, null);
            keys.Add(key);
            prev = key;
        }

        List<string> sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void GenerateKeyBetween_1000SequentialPrepends_AllSortedCorrectly()
    {
        List<string> keys = new(1000);
        string? next = null;

        for (int i = 0; i < 1000; i++)
        {
            string key = FractionalIndexing.GenerateKeyBetween(null, next);
            keys.Add(key);
            next = key;
        }

        keys.Reverse();
        List<string> sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void GenerateKeyBetween_500AlternatingInserts_AllSortedCorrectly()
    {
        string a = FractionalIndexing.GenerateKeyBetween(null, null);
        string b = FractionalIndexing.GenerateKeyBetween(a, null);

        List<string> keys = [a, b];

        for (int i = 0; i < 500; i++)
        {
            string mid = FractionalIndexing.GenerateKeyBetween(a, b);
            keys.Insert(keys.IndexOf(b), mid);

            if (i % 2 == 0)
            {
                b = mid;
            }
            else
            {
                a = mid;
            }
        }

        List<string> sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(keys, sorted);
    }

    [Fact]
    public void GenerateKeyBetween_AllKeysUnique_After1000Appends()
    {
        HashSet<string> keys = [];
        string? prev = null;

        for (int i = 0; i < 1000; i++)
        {
            string key = FractionalIndexing.GenerateKeyBetween(prev, null);
            Assert.DoesNotContain(key, keys);
            keys.Add(key);
            prev = key;
        }
    }

    [Fact]
    public void GenerateKeyBetween_MaxKeyLengthReasonable_After1000Prepends()
    {
        string? next = null;
        int maxLen = 0;

        for (int i = 0; i < 1000; i++)
        {
            string key = FractionalIndexing.GenerateKeyBetween(null, next);
            maxLen = Math.Max(maxLen, key.Length);
            next = key;
        }

        Assert.True(maxLen <= 50, $"Max key length {maxLen} exceeds 50 after 1000 prepends.");
    }

    // ─── Pool fallback / large key paths ──────────────────────────────────────

    [Fact]
    public void GenerateKeyBetween_LongKeysAlternatingInserts_StayOrdered()
    {
        // Many alternating mid-insertions eventually grow key length past the stack
        // buffer threshold (256 chars), exercising the ArrayPool fallback.
        string a = "a0";
        string b = "a1";
        int maxLen = 0;
        bool toggle = false;

        for (int i = 0; i < 3000; i++)
        {
            string mid = FractionalIndexing.GenerateKeyBetween(a, b);
            Assert.True(string.CompareOrdinal(a, mid) < 0);
            Assert.True(string.CompareOrdinal(mid, b) < 0);
            maxLen = Math.Max(maxLen, mid.Length);
            if (toggle)
            {
                a = mid;
            }
            else
            {
                b = mid;
            }

            toggle = !toggle;
        }

        Assert.True(maxLen > 256, $"Expected pool to be exercised; observed max length {maxLen}.");
    }

    // ─── GenerateNKeysBetween ─────────────────────────────────────────────────

    [Fact]
    public void GenerateNKeysBetween_GeneratesCorrectCount()
    {
        Assert.Equal(5, FractionalIndexing.GenerateNKeysBetween(null, null, 5).Length);
    }

    [Fact]
    public void GenerateNKeysBetween_AllKeysSortedAndUnique()
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween(null, null, 100);
        string[] sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(keys, sorted);
        Assert.Equal(keys.Distinct().Count(), keys.Length);
    }

    [Fact]
    public void GenerateNKeysBetween_BetweenTwoKeys_AllInRange()
    {
        const string a = "a0";
        const string b = "a1";
        string[] keys = FractionalIndexing.GenerateNKeysBetween(a, b, 10);

        foreach (string key in keys)
        {
            Assert.True(string.CompareOrdinal(key, a) > 0, $"Key '{key}' not > '{a}'");
            Assert.True(string.CompareOrdinal(key, b) < 0, $"Key '{key}' not < '{b}'");
        }
    }

    [Fact]
    public void GenerateNKeysBetween_ZeroCount_ReturnsEmpty()
    {
        Assert.Empty(FractionalIndexing.GenerateNKeysBetween(null, null, 0));
    }

    [Fact]
    public void GenerateNKeysBetween_LeftOpenBound_GeneratesDescending()
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween(null, "a0", 10);
        Assert.Equal(10, keys.Length);
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0,
                $"Keys not sorted: '{keys[i - 1]}' >= '{keys[i]}'");
        }

        Assert.True(string.CompareOrdinal(keys[^1], "a0") < 0);
    }

    [Fact]
    public void GenerateNKeysBetween_RightOpenBound_GeneratesAscending()
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween("a0", null, 10);
        Assert.Equal(10, keys.Length);
        Assert.True(string.CompareOrdinal("a0", keys[0]) < 0);
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0);
        }
    }

    [Fact]
    public void GenerateNKeysBetween_BothOpen_GeneratesEvenlyAcrossSpace()
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween(null, null, 31);
        Assert.Equal(31, keys.Length);
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0);
        }
    }

    [Fact]
    public void GenerateNKeysBetween_SingleKey_DelegatesToSingle()
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween("a0", "a1", 1);
        Assert.Single(keys);
        Assert.Equal(FractionalIndexing.GenerateKeyBetween("a0", "a1"), keys[0]);
    }

    // ─── Stress / soak ────────────────────────────────────────────────────────

    [Fact]
#pragma warning disable CA5394 // Deterministic seeded Random is fine for test fixtures.
    public void GenerateKeyBetween_RandomInsertions_NeverLosesOrder()
    {
        Random rng = new(12345);
        List<string> keys = [FractionalIndexing.GenerateKeyBetween(null, null)];

        for (int i = 0; i < 2000; i++)
        {
            int pos = rng.Next(0, keys.Count + 1);
            string? lower = pos > 0 ? keys[pos - 1] : null;
            string? upper = pos < keys.Count ? keys[pos] : null;
            string mid = FractionalIndexing.GenerateKeyBetween(lower, upper);
            keys.Insert(pos, mid);
        }

        for (int i = 1; i < keys.Count; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0,
                $"Order broken at {i}: '{keys[i - 1]}' >= '{keys[i]}'");
        }

        Assert.Equal(keys.Distinct().Count(), keys.Count);
    }
#pragma warning restore CA5394
}
