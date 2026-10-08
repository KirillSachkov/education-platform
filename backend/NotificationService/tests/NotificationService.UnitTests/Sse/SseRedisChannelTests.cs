using NotificationService.Core.Sse;

namespace NotificationService.UnitTests.Sse;

/// <summary>
/// Round-trip: <c>For(userId)</c> должен быть обратно парсибельным через <c>TryExtractUserId</c>.
/// Проверяем edge cases (пустая строка, другой префикс, битый userId).
/// </summary>
public sealed class SseRedisChannelTests
{
    [Fact]
    public void For_UsesNFormat()
    {
        Guid userId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        string channel = SseRedisChannel.For(userId);
        // N-format = 32 hex chars без дефисов.
        Assert.Equal("notifications:sse:user:" + userId.ToString("N"), channel);
        Assert.EndsWith("12345678123412341234123456789abc", channel, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractUserId_RoundTrip()
    {
        Guid userId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        string channel = SseRedisChannel.For(userId);
        Guid? parsed = SseRedisChannel.TryExtractUserId(channel);
        Assert.Equal(userId, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("notifications:sse:other:abc")]
    [InlineData("notifications:sse:user:not-a-guid")]
    public void TryExtractUserId_InvalidInput_ReturnsNull(string bad)
    {
        Assert.Null(SseRedisChannel.TryExtractUserId(bad));
    }
}
