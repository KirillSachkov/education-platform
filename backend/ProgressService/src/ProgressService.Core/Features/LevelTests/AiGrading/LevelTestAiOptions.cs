namespace ProgressService.Core.Features.LevelTests.AiGrading;

/// <summary>
///     Config-binding AI-грейдинга открытых ответов level-test'а (ST-5, #480).
///     Секция <c>LevelTestAi</c>. Провайдер-слой конфигурируется отдельно в секции
///     <c>AI</c> (Shared/AI multi-provider, ключ через env
///     <c>AI__PROVIDERS__AITUNNEL__APIKEY</c> — тот же, что у ARS/MPS).
/// </summary>
public sealed class LevelTestAiOptions
{
    public const string SECTION_NAME = "LevelTestAi";

    /// <summary>
    ///     Master-тумблер AI-грейдинга. Config-дефолт = <c>false</c> (выключено):
    ///     handler не зовёт AI и сразу помечает попытку FAILED (choice-only проценты
    ///     остаются честным результатом) — попытка никогда не зависает в QUEUED.
    /// </summary>
    public bool Enabled { get; set; }

    public string Model { get; set; } = "openai/gpt-4.1-mini";

    public double Temperature { get; set; }

    public int MaxOutputTokens { get; set; } = 4000;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    ///     Сколько раз пытаемся прогнать AI-вызов (включая первый) прежде чем
    ///     зафиксировать FAILED. Зеркало ARS <c>Limits.LlmMaxAttempts</c> (#405).
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    ///     База паузы между ретраями в секундах: пауза перед попыткой N = N × значение
    ///     (линейный backoff, как у ARS <c>LlmRetryDelaySeconds</c>). 0 — ретраить
    ///     мгновенно (интеграционные тесты).
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;
}
