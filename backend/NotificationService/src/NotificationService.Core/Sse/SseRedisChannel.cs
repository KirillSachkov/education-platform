using System.Globalization;

namespace NotificationService.Core.Sse;

/// <summary>
/// Общие константы Redis Pub/Sub для SSE-fanout'а.
/// Channel format: <c>notifications:sse:user:{userId}</c>. Subscriber pattern: <c>notifications:sse:user:*</c>.
/// Префикс <c>notifications:sse:</c> — чтобы не пересекаться с Redis-ключами ContentAccess
/// (<c>access:*</c>) и HybridCache.
/// </summary>
public static class SseRedisChannel
{
    public const string PREFIX = "notifications:sse:user:";
    public const string PATTERN = "notifications:sse:user:*";

    public static string For(Guid userId) =>
        PREFIX + userId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Парсит channel name вида <c>notifications:sse:user:{userId}</c> → <c>userId</c>.
    /// Возвращает <c>null</c> если формат некорректный (игнорируем, это не наш канал).
    /// </summary>
    public static Guid? TryExtractUserId(string channelName)
    {
        if (string.IsNullOrEmpty(channelName) ||
            !channelName.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        string userIdPart = channelName[PREFIX.Length..];
        return Guid.TryParseExact(userIdPart, "N", out Guid userId) ? userId : null;
    }
}
