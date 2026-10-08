namespace NotificationService.Core.Sse;

/// <summary>
/// Настройки SSE (секция <c>Sse</c> в appsettings).
///
/// <see cref="RedisFanoutEnabled"/>: если <c>true</c>, <c>SseFanoutHandler</c> публикует в Redis
/// вместо прямого push'а в локальный hub, а <c>SseRedisSubscriberService</c> на каждой реплике
/// ловит событие и пушит в свой hub. Нужно когда >1 реплика NotificationService.
///
/// В dev / single-replica держим <c>false</c> — лишний Redis round-trip не нужен.
/// </summary>
public sealed class SseOptions
{
    public const string SECTION_NAME = "Sse";

    public bool RedisFanoutEnabled { get; init; }
}
