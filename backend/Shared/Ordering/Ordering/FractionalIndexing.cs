using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ordering.Tests")]

namespace Ordering;

/// <summary>
///     Fractional indexing algorithm for generating lexicographically sortable order keys.
///     Ultra-optimized .NET implementation: zero-copy spans, stackalloc-first, ArrayPool fallback,
///     iterative midpoint, O(1) char→digit lookup table.
///     Spec-compatible with rocicorp/fractional-indexing (CC0). Base-62: 0-9 A-Z a-z.
/// </summary>
public static class FractionalIndexing
{
    public const string Base62Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // "A" + 26 zeros = length 27. Cannot exist as a complete order key.
    internal const string SmallestInteger = "A00000000000000000000000000";

    private const int Base = 62;
    private const byte Invalid = 255;
    private const char ZeroDigit = '0';
    private const char MaxDigit = 'z';

    // Covers >99% of real-world keys without leaving the stack.
    private const int StackBufferSize = 256;

    // Max integer-part length is 27 ('a' or 'A'). 64 is a safe scratch buffer for any
    // increment/decrement intermediate.
    private const int IntScratchSize = 64;

    // 128-byte ASCII → base62 digit lookup table.
    // C# compiler emits byte[] literals as RVA data — no heap allocation, cache-friendly.
    private static ReadOnlySpan<byte> DigitTable =>
    [
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9,                                       // '0'..'9'
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        10, 11, 12, 13, 14, 15, 16, 17, 18, 19,                             // 'A'..'J'
        20, 21, 22, 23, 24, 25, 26, 27, 28, 29,                             // 'K'..'T'
        30, 31, 32, 33, 34, 35,                                             // 'U'..'Z'
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid,
        36, 37, 38, 39, 40, 41, 42, 43, 44, 45,                             // 'a'..'j'
        46, 47, 48, 49, 50, 51, 52, 53, 54, 55,                             // 'k'..'t'
        56, 57, 58, 59, 60, 61,                                             // 'u'..'z'
        Invalid, Invalid, Invalid, Invalid, Invalid,
    ];

    /// <summary>
    ///     Generates a single order key strictly between <paramref name="a" /> and <paramref name="b" />.
    ///     Pass null for an open lower or upper bound. Both null → returns "a0".
    /// </summary>
    /// <exception cref="ArgumentException">If <paramref name="a" /> &gt;= <paramref name="b" /> or either key is malformed.</exception>
    public static string GenerateKeyBetween(string? a, string? b)
    {
        int upperBound = Math.Max(a?.Length ?? 0, b?.Length ?? 0) + 32;

        if (upperBound <= StackBufferSize)
        {
            Span<char> buf = stackalloc char[StackBufferSize];
            int written = GenerateKeyBetweenCore(a.AsSpan(), a is not null, b.AsSpan(), b is not null, buf);
            return new string(buf[..written]);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(upperBound);
        try
        {
            int written = GenerateKeyBetweenCore(a.AsSpan(), a is not null, b.AsSpan(), b is not null, rented);
            return new string(rented, 0, written);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    ///     Generates <paramref name="n" /> strictly-ordered keys evenly distributed between two bounds.
    /// </summary>
    public static string[] GenerateNKeysBetween(string? a, string? b, int n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Count must be non-negative.");
        }

        if (n == 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [GenerateKeyBetween(a, b)];
        }

        string[] result = new string[n];
        FillNKeysBetween(a, b, result.AsSpan());
        return result;
    }

    /// <summary>
    ///     Validates an order key without throwing. Returns false on null/empty/malformed input.
    /// </summary>
    public static bool IsValidOrderKey([NotNullWhen(true)] string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        ReadOnlySpan<char> k = key.AsSpan();
        if (k.SequenceEqual(SmallestInteger))
        {
            return false;
        }

        int intLen = TryIntegerLength(k[0]);
        if (intLen < 0 || intLen > k.Length)
        {
            return false;
        }

        for (int i = 1; i < k.Length; i++)
        {
            if (TryDigit(k[i]) < 0)
            {
                return false;
            }
        }

        return !(k.Length > intLen && k[^1] == ZeroDigit);
    }

    // ─── Internals (visible to tests via InternalsVisibleTo) ──────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int DigitValue(char c)
    {
        if (c >= 128)
        {
            throw new ArgumentException($"Invalid base62 digit: '{c}'.", nameof(c));
        }

        byte v = DigitTable[c];
        if (v == Invalid)
        {
            throw new ArgumentException($"Invalid base62 digit: '{c}'.", nameof(c));
        }

        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TryDigit(char c)
    {
        if (c >= 128)
        {
            return -1;
        }

        byte v = DigitTable[c];
        return v == Invalid ? -1 : v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static char DigitChar(int idx) => Base62Digits[idx];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int IntegerLength(char head)
    {
        if ((uint)(head - 'a') <= 'z' - 'a')
        {
            return head - 'a' + 2;
        }

        if ((uint)(head - 'A') <= 'Z' - 'A')
        {
            return 'Z' - head + 2;
        }

        throw new ArgumentException($"Invalid order key head character: '{head}'.", nameof(head));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TryIntegerLength(char head)
    {
        if ((uint)(head - 'a') <= 'z' - 'a')
        {
            return head - 'a' + 2;
        }

        if ((uint)(head - 'A') <= 'Z' - 'A')
        {
            return 'Z' - head + 2;
        }

        return -1;
    }

    /// <summary>
    ///     Increments an integer part. Writes result into <paramref name="dest" />. Returns chars written,
    ///     or -1 if overflow ("zz…z" — cannot grow further).
    /// </summary>
    internal static int IncrementInteger(ReadOnlySpan<char> x, Span<char> dest)
    {
        if (IntegerLength(x[0]) != x.Length)
        {
            throw new ArgumentException($"Invalid integer part of order key: '{x}'.", nameof(x));
        }

        char head = x[0];
        int len = x.Length;
        x.CopyTo(dest);

        bool carry = true;
        for (int i = len - 1; carry && i >= 1; i--)
        {
            int d = DigitValue(dest[i]) + 1;
            if (d == Base)
            {
                dest[i] = ZeroDigit;
            }
            else
            {
                dest[i] = DigitChar(d);
                carry = false;
            }
        }

        if (!carry)
        {
            return len;
        }

        // Carry overflowed the integer part: bump head, possibly resize.
        if (head == 'Z')
        {
            dest[0] = 'a';
            dest[1] = ZeroDigit;
            return 2;
        }

        if (head == 'z')
        {
            return -1;
        }

        char h = (char)(head + 1);
        dest[0] = h;

        if (h > 'a')
        {
            // Lowercase grow: 'a'..'y' → 'b'..'z'. Append a zero.
            dest[len] = ZeroDigit;
            return len + 1;
        }

        // Uppercase shrink: 'A'..'Y' → 'B'..'Z'. Drop trailing digit.
        return len - 1;
    }

    /// <summary>
    ///     Decrements an integer part. Returns chars written, or -1 if underflow ("A0…0").
    /// </summary>
    internal static int DecrementInteger(ReadOnlySpan<char> x, Span<char> dest)
    {
        if (IntegerLength(x[0]) != x.Length)
        {
            throw new ArgumentException($"Invalid integer part of order key: '{x}'.", nameof(x));
        }

        char head = x[0];
        int len = x.Length;
        x.CopyTo(dest);

        bool borrow = true;
        for (int i = len - 1; borrow && i >= 1; i--)
        {
            int d = DigitValue(dest[i]) - 1;
            if (d == -1)
            {
                dest[i] = MaxDigit;
            }
            else
            {
                dest[i] = DigitChar(d);
                borrow = false;
            }
        }

        if (!borrow)
        {
            return len;
        }

        if (head == 'a')
        {
            dest[0] = 'Z';
            dest[1] = MaxDigit;
            return 2;
        }

        if (head == 'A')
        {
            return -1;
        }

        char h = (char)(head - 1);
        dest[0] = h;

        if (h < 'Z')
        {
            // Uppercase grow: 'B'..'Z' → 'A'..'Y'. Append a max-digit.
            dest[len] = MaxDigit;
            return len + 1;
        }

        // Lowercase shrink: 'b'..'z' → 'a'..'y'. Drop trailing digit.
        return len - 1;
    }

    /// <summary>
    ///     Computes the midpoint of two fractional parts iteratively.
    ///     <paramref name="a" /> is the lower bound (treated as zero-padded if shorter).
    ///     If <paramref name="hasB" /> is false, <paramref name="b" /> represents "+infinity".
    /// </summary>
    internal static int Midpoint(ReadOnlySpan<char> a, ReadOnlySpan<char> b, bool hasB, Span<char> dest)
    {
        if (hasB && a.SequenceCompareTo(b) >= 0)
        {
            throw new ArgumentException($"Midpoint requires a < b, got '{a}' >= '{b}'.", nameof(a));
        }

        int written = 0;

        while (true)
        {
            if (hasB)
            {
                // Skip the common prefix of a and b. When a is shorter, treat missing chars as '0'.
                int n = 0;
                int common = Math.Min(a.Length, b.Length);
                while (n < common && a[n] == b[n])
                {
                    n++;
                }

                if (n == a.Length)
                {
                    while (n < b.Length && b[n] == ZeroDigit)
                    {
                        n++;
                    }
                }

                if (n > 0)
                {
                    b[..n].CopyTo(dest[written..]);
                    written += n;
                    a = a.Length > n ? a[n..] : default;
                    b = b[n..];
                    continue;
                }
            }

            int digitA = a.Length > 0 ? DigitValue(a[0]) : 0;
            int digitB = hasB ? DigitValue(b[0]) : Base;

            if (digitB - digitA > 1)
            {
                // Round-half-up (matches the rocicorp JS reference).
                int midDigit = (digitA + digitB + 1) >> 1;
                dest[written++] = DigitChar(midDigit);
                return written;
            }

            if (hasB && b.Length > 1)
            {
                // Consecutive digits and b has more depth: emit b[0] and the gap opens below.
                dest[written++] = b[0];
                return written;
            }

            // Emit digit_a, advance a, drop the b bound (now -∞..+∞ range for the suffix).
            dest[written++] = DigitChar(digitA);
            a = a.Length > 1 ? a[1..] : default;
            b = default;
            hasB = false;
        }
    }

    internal static void ValidateOrderKey(ReadOnlySpan<char> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Order key must not be empty.", nameof(key));
        }

        if (key.SequenceEqual(SmallestInteger))
        {
            throw new ArgumentException("Order key cannot equal the smallest integer sentinel.", nameof(key));
        }

        int intLen = IntegerLength(key[0]);
        if (intLen > key.Length)
        {
            throw new ArgumentException($"Invalid order key: '{key}'.", nameof(key));
        }

        // Validate every char is a legal base-62 digit. Head was checked by IntegerLength.
        for (int i = 1; i < key.Length; i++)
        {
            _ = DigitValue(key[i]);
        }

        if (key.Length > intLen && key[^1] == ZeroDigit)
        {
            throw new ArgumentException($"Order key fractional part must not end with '{ZeroDigit}'.", nameof(key));
        }
    }

    // ─── Core implementations ─────────────────────────────────────────────────

    private static int GenerateKeyBetweenCore(
        ReadOnlySpan<char> a, bool hasA,
        ReadOnlySpan<char> b, bool hasB,
        Span<char> dest)
    {
        if (hasA)
        {
            ValidateOrderKey(a);
        }

        if (hasB)
        {
            ValidateOrderKey(b);
        }

        if (hasA && hasB && a.SequenceCompareTo(b) >= 0)
        {
            throw new ArgumentException($"Order key '{a}' must be less than '{b}'.", nameof(a));
        }

        if (!hasA)
        {
            return PrependCore(b, hasB, dest);
        }

        if (!hasB)
        {
            return AppendCore(a, dest);
        }

        return BetweenCore(a, b, dest);
    }

    private static int PrependCore(ReadOnlySpan<char> b, bool hasB, Span<char> dest)
    {
        if (!hasB)
        {
            dest[0] = 'a';
            dest[1] = ZeroDigit;
            return 2;
        }

        int ibLen = IntegerLength(b[0]);
        if (ibLen > b.Length)
        {
            throw new ArgumentException($"Invalid order key: '{b}'.", nameof(b));
        }

        ReadOnlySpan<char> ib = b[..ibLen];
        ReadOnlySpan<char> fb = b[ibLen..];

        if (ib.SequenceEqual(SmallestInteger))
        {
            ib.CopyTo(dest);
            int mid = Midpoint(default, fb, hasB: true, dest[ibLen..]);
            return ibLen + mid;
        }

        if (fb.Length > 0)
        {
            ib.CopyTo(dest);
            return ibLen;
        }

        int decLen = DecrementInteger(ib, dest);
        if (decLen < 0)
        {
            throw new InvalidOperationException("Cannot decrement order key integer below minimum.");
        }

        return decLen;
    }

    private static int AppendCore(ReadOnlySpan<char> a, Span<char> dest)
    {
        int iaLen = IntegerLength(a[0]);
        if (iaLen > a.Length)
        {
            throw new ArgumentException($"Invalid order key: '{a}'.", nameof(a));
        }

        ReadOnlySpan<char> ia = a[..iaLen];
        ReadOnlySpan<char> fa = a[iaLen..];

        int incLen = IncrementInteger(ia, dest);
        if (incLen >= 0)
        {
            return incLen;
        }

        ia.CopyTo(dest);
        int mid = Midpoint(fa, default, hasB: false, dest[iaLen..]);
        return iaLen + mid;
    }

    private static int BetweenCore(ReadOnlySpan<char> a, ReadOnlySpan<char> b, Span<char> dest)
    {
        int iaLen = IntegerLength(a[0]);
        int ibLen = IntegerLength(b[0]);
        if (iaLen > a.Length)
        {
            throw new ArgumentException($"Invalid order key: '{a}'.", nameof(a));
        }

        if (ibLen > b.Length)
        {
            throw new ArgumentException($"Invalid order key: '{b}'.", nameof(b));
        }

        ReadOnlySpan<char> ia = a[..iaLen];
        ReadOnlySpan<char> fa = a[iaLen..];
        ReadOnlySpan<char> ib = b[..ibLen];
        ReadOnlySpan<char> fb = b[ibLen..];

        if (ia.SequenceEqual(ib))
        {
            ia.CopyTo(dest);
            int midSame = Midpoint(fa, fb, hasB: true, dest[iaLen..]);
            return iaLen + midSame;
        }

        Span<char> incBuf = stackalloc char[IntScratchSize];
        int incLen = IncrementInteger(ia, incBuf);
        if (incLen >= 0)
        {
            ReadOnlySpan<char> inc = incBuf[..incLen];
            if (inc.SequenceCompareTo(b) < 0)
            {
                inc.CopyTo(dest);
                return incLen;
            }
        }

        ia.CopyTo(dest);
        int midFallback = Midpoint(fa, default, hasB: false, dest[iaLen..]);
        return iaLen + midFallback;
    }

    private static void FillNKeysBetween(string? a, string? b, Span<string> result)
    {
        int n = result.Length;
        if (n == 0)
        {
            return;
        }

        if (n == 1)
        {
            result[0] = GenerateKeyBetween(a, b);
            return;
        }

        if (b is null)
        {
            string c = GenerateKeyBetween(a, null);
            result[0] = c;
            for (int i = 1; i < n; i++)
            {
                c = GenerateKeyBetween(c, null);
                result[i] = c;
            }

            return;
        }

        if (a is null)
        {
            string c = GenerateKeyBetween(null, b);
            result[n - 1] = c;
            for (int i = n - 2; i >= 0; i--)
            {
                c = GenerateKeyBetween(null, c);
                result[i] = c;
            }

            return;
        }

        int mid = n / 2;
        string midKey = GenerateKeyBetween(a, b);
        FillNKeysBetween(a, midKey, result[..mid]);
        result[mid] = midKey;
        FillNKeysBetween(midKey, b, result[(mid + 1)..]);
    }
}
