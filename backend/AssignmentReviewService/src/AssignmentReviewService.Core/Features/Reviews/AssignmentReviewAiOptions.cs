namespace AssignmentReviewService.Core.Features.Reviews;

/// <summary>
///     Config-binding для AI slot'а Reviewer. Section <c>AssignmentReviewAI</c>.
///     Phase 11 admin override (singleton aggregate в БД) перекрывает значения
///     отсюда — но base config — это appsettings.{Environment}.json.
/// </summary>
public sealed class AssignmentReviewAiOptions
{
    public const string SECTION_NAME = "AssignmentReviewAI";

    /// <summary>
    ///     Платформенный тумблер AI-проверки PR (#355). Config-дефолт = <c>false</c>
    ///     (выключено). DB-singleton (admin override через PUT /admin/ai-settings/)
    ///     перекрывает. Когда выключено — новые submission'ы не создают AiReview и не
    ///     запускают авто-ревью, ручной re-run отбивается <c>review.disabled</c>.
    /// </summary>
    public bool ReviewEnabled { get; set; }

    public AssignmentReviewAiSlot Reviewer { get; set; } = new();

    /// <summary>
    ///     Глобальный base review prompt (#15) — config-дефолт, используемый когда
    ///     admin не задал свой через <c>ai_model_settings.reviewer_base_prompt</c>.
    ///     Подаётся reviewer'у как TRUSTED instructions block сразу после system prompt'а.
    /// </summary>
    public string ReviewerBasePrompt { get; set; } =
        "Если студент оставил в PR/диффе вопросы или TODO к ревьюеру — ответь на них в summary или inline-комментах.";

    /// <summary>
    ///     Лимиты пайплайна. Per-iteration / per-user / per-submission caps —
    ///     domain-level rate limiting в <c>RateLimitChecker</c>. Per-IP /
    ///     per-window — middleware в Program.cs (rate-limit policy).
    /// </summary>
    public AssignmentReviewLimits Limits { get; set; } = new();

    /// <summary>
    ///     Дозапрос файлов репозитория ревьюером (#798): карта репо в промпте +
    ///     bounded-цикл need_files. Config-дефолт флага перекрывается DB-singleton'ом
    ///     <c>ai_model_settings.repo_context_enabled</c>.
    /// </summary>
    public AssignmentReviewRepoContextOptions RepoContext { get; set; } = new();
}

/// <summary>
///     Потолки цикла дозапроса файлов (#798). Все капы жёсткие: их исчерпание не
///     роняет итерацию — модель обязана вынести вердикт из уже собранного контекста.
/// </summary>
public sealed class AssignmentReviewRepoContextOptions
{
    /// <summary>Config-дефолт фиче-флага. DB-singleton (admin PUT) перекрывает. Default OFF.</summary>
    public bool Enabled { get; set; }

    /// <summary>Максимум ДОПОЛНИТЕЛЬНЫХ LLM-раундов после первого вызова (1 раунд = 1 дозапрос файлов).</summary>
    public int MaxExtraRounds { get; set; } = 2;

    /// <summary>Суммарный кап дозапрошенных файлов на одну итерацию ревью.</summary>
    public int MaxTotalFiles { get; set; } = 8;

    /// <summary>Максимум путей в одном need_files-ответе модели (излишек обрезается).</summary>
    public int MaxFilesPerRound { get; set; } = 5;

    /// <summary>Кап содержимого одного дозапрошенного файла (байты UTF-8; сверх — файл не передаётся).</summary>
    public int MaxFileBytes { get; set; } = 51_200;

    /// <summary>Сколько манифестов зависимостей (*.csproj, package.json, …) класть в карту репо.</summary>
    public int MaxManifestFiles { get; set; } = 5;

    /// <summary>Кап содержимого одного манифеста (байты; сверх — обрезается с пометкой).</summary>
    public int MaxManifestBytes { get; set; } = 16_384;

    /// <summary>Кап текстового представления дерева файлов в промпте (символы; сверх — обрезается с пометкой).</summary>
    public int MaxTreeChars { get; set; } = 12_000;
}

public sealed class AssignmentReviewAiSlot
{
    public string Model { get; set; } = "openai/gpt-4.1-mini";

    public double Temperature { get; set; } = 0.2;

    public int MaxOutputTokens { get; set; } = 4000;

    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class AssignmentReviewLimits
{
    /// <summary>
    ///     Максимум добавленных строк в diff'е на ОДИН LLM batch. Diff больше этого
    ///     порога разбивается на последовательные batch'и (#18) — каждый ≤ лимита.
    ///     До #18 это был hard-reject порог (DIFF_TOO_LARGE).
    /// </summary>
    public int MaxDiffAdditions { get; set; } = 1500;

    /// <summary>
    ///     Жёсткий верхний потолок (#18) на reviewable additions ПОСЛЕ фильтрации
    ///     non-reviewable файлов. Выше — graceful fail (DIFF_TOO_LARGE), просим
    ///     разбить PR. Защищает от cost-blowup'а на гигантских PR'ах.
    ///     8000 → 20000 в #546: реальные студенческие PR'ы (DirectoryService#56 —
    ///     6539 строк / 112 файлов) спотыкались о капы; deepseek-дешёвый chunked-прогон
    ///     20k строк ≈ 14 batches — приемлемо.
    /// </summary>
    public int HardMaxDiffAdditions { get; set; } = 20000;

    /// <summary>
    ///     Жёсткий верхний потолок (#18) на количество reviewable файлов после
    ///     фильтрации. Выше — graceful fail (DIFF_TOO_LARGE). 60 → 150 в #546.
    /// </summary>
    public int HardMaxFiles { get; set; } = 150;

    /// <summary>
    ///     Emergency cost ceiling for an author/admin manual run. Manual runs may exceed
    ///     the automatic cap, but must remain finite to prevent unbounded LLM batches.
    /// </summary>
    public int ManualMaxDiffAdditions { get; set; } = 100_000;

    /// <summary>Emergency file-count ceiling for an author/admin manual run.</summary>
    public int ManualMaxFiles { get; set; } = 1_000;

    /// <summary>Максимум iteration'ов на одну submission (anti-flood).</summary>
    public int MaxIterationsPerSubmission { get; set; } = 20;

    /// <summary>Максимум iteration'ов от одного юзера за 24ч (cost guard).</summary>
    public int MaxIterationsPerUserPerDay { get; set; } = 100;

    /// <summary>
    ///     Максимум iteration'ов на одного автора (через все его курсы) за 24ч.
    ///     Issue #327 — защита от cost-blowup'ов при популярном курсе (1000 студентов
    ///     с одной задачей × 100 каждый = 100k запросов/день, неприемлемо).
    ///     0 или -1 = без cap'а (только per-user).
    /// </summary>
    public int MaxIterationsPerAuthorPerDay { get; set; } = 500;

    /// <summary>Максимум inline-комментариев AI (truncate если больше).</summary>
    public int MaxInlineComments { get; set; } = 12;

    /// <summary>
    ///     Сколько раз пытаемся прогнать LLM-вызов при транзиентном сбое провайдера
    ///     (review.llm.unavailable) прежде чем зафиксировать FAILED (#405). 1 = без ретраев.
    /// </summary>
    public int LlmMaxAttempts { get; set; } = 3;

    /// <summary>
    ///     База паузы между LLM-ретраями в секундах (#405). Пауза перед попыткой N =
    ///     N × LlmRetryDelaySeconds (линейный backoff: 10s, 20s, ...). 0 = ретраить мгновенно
    ///     (интеграционные тесты выставляют 0, чтобы не спать).
    /// </summary>
    public int LlmRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    ///     Reviews older than this in QUEUED/RUNNING are considered stale by the
    ///     recovery watchdog and are requeued. Startup recovery still resets every
    ///     RUNNING row because previous in-process workers are gone after restart.
    ///     #690: 60 → 15 — students should not wait up to an hour for a stuck review
    ///     to auto-recover. For RUNNING rows staleness is measured by
    ///     <c>GREATEST(updated_at, heartbeat_at)</c>, so a legitimately long multi-batch
    ///     review that keeps heart-beating is never mistaken for stuck — only a review
    ///     with no progress for this window is requeued.
    /// </summary>
    public int StaleReviewMaxAgeMinutes { get; set; } = 15;

    /// <summary>How often the stale review watchdog scans for stuck QUEUED/RUNNING reviews.</summary>
    public int StaleRecoveryIntervalSeconds { get; set; } = 300;
}
