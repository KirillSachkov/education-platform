namespace TrainerService.Core.Configuration;

/// <summary>
///     Service-specific AI settings for the trainer (#585): voice transcription (Whisper STT)
///     and open-answer grading (LLM). Bound from the <c>TrainerAI</c> config section. The provider
///     wiring itself lives under the shared <c>AI</c> section (multi-provider factory).
/// </summary>
public sealed class TrainerAiOptions
{
    public const string SECTION_NAME = "TrainerAI";

    public TrainerTranscriptionOptions Transcription { get; set; } = new();

    public TrainerGradingOptions Grading { get; set; } = new();

    /// <summary>Per-model pricing for the AI-usage ledger (#614 C1). Bound from <c>TrainerAI:Pricing</c>.</summary>
    public TrainerAiPricingOptions Pricing { get; set; } = new();

    /// <summary>Per-user AI-usage quota limits (#614 C2). Bound from <c>TrainerAI:Limits</c>.</summary>
    public TrainerAiLimitsOptions Limits { get; set; } = new();

    /// <summary>
    ///     Burst cap for inline OPEN_TEXT AI grading: max billable open-answer checks per user per
    ///     minute. This is separate from daily/monthly quota: quota protects budget, this protects the
    ///     provider from rapid retries. &lt;= 0 disables the burst limiter.
    /// </summary>
    public int OpenGradeRateLimitPerMinute { get; set; } = 20;

    /// <summary>
     ///     Server-side cap on an uploaded voice answer, in bytes (#614 C2). Rejected BEFORE transcription
     ///     as a coarse audio-duration proxy (≈1 min of opus ≈ 0.5–1 MiB) — a second, tighter guard on top
    ///     of the absolute 25 MiB Whisper limit, so a non-PRO-bounded user can't burn STT budget with a
    ///     long file. Default 8 MiB is a PLACEHOLDER — the owner tunes it later.
    /// </summary>
    public long MaxAudioBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    ///     Max duration of a SINGLE voice answer, in seconds (#663). The primary enforcement is on the
    ///     client (the recorder auto-stops at this length and shows «до N мин»); the server side keeps
    ///     <see cref="MaxAudioBytes"/> as a coarse byte backstop. Used to phrase the user-facing «слишком
    ///     длинная запись» message and to size the per-answer minutes consumed from the VOICE quota.
    ///     Default 180s (3 min) — owner-confirmed, tunable in config.
    /// </summary>
    public int MaxVoiceAnswerSeconds { get; set; } = 180;
}

/// <summary>
///     Per-user AI-usage quotas (#614 C2), split by tier. <see cref="Free"/> applies to a user WITHOUT the
///     <c>cap:TRAINER_PRO</c> capability; <see cref="Pro"/> to a holder. Admin bypasses both. Each limit is
///     an int; <b>&lt;= 0 means UNLIMITED</b> on that dimension (so a PRO tier can be uncapped). Defaults are
///     PLACEHOLDERS — the owner tunes them later. Bound from <c>TrainerAI:Limits</c>.
/// </summary>
public sealed class TrainerAiLimitsOptions
{
    /// <summary>
    ///     Free tier (#623): открытые (OPEN_TEXT/голос) ответы + AI-анализ + мок — ЦЕЛИКОМ за PRO.
    ///     Free-юзер хард-блокируется PRO-гейтом в хендлерах (CheckAnswer / SubmitVoiceAnswer /
    ///     StartMock*) с чистым <c>trainer.pro.required</c> ДО обращения к квоте — поэтому эти числа
    ///     для free фактически мёртвый код (free сюда не доходит). Оставлены ненулевыми намеренно:
    ///     <c>0</c> по конвенции = «unlimited», что для free было бы обратным эффектом. Закрытые
    ///     тесты (choice/exact-text, авто-грейд без AI) у free — без лимита, квотой не меряются.
    /// </summary>
    public TrainerAiTierLimits Free { get; set; } = new()
    {
        OpenGradesPerDay = 20,
        VoiceMinutesPerMonth = 0,
        MockPerMonth = 0,
    };

    /// <summary>
    ///     PRO tier (#623): конечный анти-абуз потолок по всем AI-измерениям (а НЕ безлимит), чтобы
    ///     подписчик не мог бесконечно жечь AI-бюджет. Числа — PLACEHOLDER, владелец тюнит в конфиге
    ///     без правки кода/деплоя (issue #623, отложенное owner-решение по цифрам).
    /// </summary>
    public TrainerAiTierLimits Pro { get; set; } = new()
    {
        OpenGradesPerDay = 60,
        VoiceMinutesPerMonth = 120,
        MockPerMonth = 60,
    };
}

/// <summary>One tier's per-dimension limits (#614 C2). Each &lt;= 0 ⇒ unlimited on that dimension.</summary>
public sealed class TrainerAiTierLimits
{
    /// <summary>Max inline OPEN_TEXT AI grades per calendar day (UTC). &lt;= 0 ⇒ unlimited.</summary>
    public int OpenGradesPerDay { get; set; }

    /// <summary>
    ///     Max voice-answer audio MINUTES per calendar month (UTC), #663. The VOICE quota is metered in
    ///     audio minutes (not a count of answers) so spend tracks real transcription cost; the Redis
    ///     counter stores seconds and this cap is multiplied by 60. &lt;= 0 ⇒ unlimited.
    /// </summary>
    public int VoiceMinutesPerMonth { get; set; }

    /// <summary>Max mock-interview sessions started per calendar month (UTC). &lt;= 0 ⇒ unlimited.</summary>
    public int MockPerMonth { get; set; }
}

/// <summary>
///     Per-model AI pricing for the usage ledger (#614 C1). Prices are in ₽ per 1M tokens
///     (input / output). Keys are the model ids passed to the provider (<c>gpt-4.1-mini</c>,
///     <c>gpt-4o-mini-transcribe</c>, …). Unknown model → cost 0 (logged). Defaults are
///     PLACEHOLDERS — the owner tunes them later against the actual AITunnel rate card.
/// </summary>
public sealed class TrainerAiPricingOptions
{
    /// <summary>
    ///     Model id → price. Defaults are placeholder approximations (AITunnel, see Shared/AI/CLAUDE.md):
    ///     <c>gpt-4.1-mini</c> ≈ ₽77 in / ₽307 out per 1M; <c>gpt-4o-mini-transcribe</c> ≈ ₽5.6 in /
    ///     ₽22 out per 1M (token-billed STT — но usage пока не доходит через AiTranscriptionResult,
    ///     поэтому фактически считается как 0 до перехода на token-billed STT с usage).
    /// </summary>
    public Dictionary<string, TrainerAiModelPrice> Models { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4.1-mini"] = new() { InputPerMillionRub = 77m, OutputPerMillionRub = 307m },
        ["gpt-4o-mini-transcribe"] = new() { InputPerMillionRub = 5.6m, OutputPerMillionRub = 22m },
    };

    /// <summary>
    ///     Per-minute ₽ price used to ESTIMATE the cost of a transcription call (#614 C2). STT is in
    ///     practice token-billed (<c>gpt-4o-mini-transcribe</c>), but <see cref="Shared.AI.AiTranscriptionResult"/>
    ///     carries no token usage — so per-minute is the best available proxy (normalized provider
    ///     duration). Placeholder default ≈ ₽1.15/min (whisper-1 rate card) — owner-tunable.
    /// </summary>
    public decimal TranscriptionPerMinuteRub { get; set; } = 1.15m;
}

/// <summary>Цена одной модели: ₽ за 1M входных / выходных токенов.</summary>
public sealed class TrainerAiModelPrice
{
    public decimal InputPerMillionRub { get; set; }

    public decimal OutputPerMillionRub { get; set; }
}

/// <summary>STT settings — model used to transcribe a candidate's spoken answer.</summary>
public sealed class TrainerTranscriptionOptions
{
    public string Model { get; set; } = "whisper-1";
}

/// <summary>LLM settings for grading open answers + producing the overall mock-interview feedback.</summary>
public sealed class TrainerGradingOptions
{
    public string Model { get; set; } = "gpt-4.1-mini";

    public double Temperature { get; set; } = 0.2;

    public int MaxOutputTokens { get; set; } = 1500;

    public int TimeoutSeconds { get; set; } = 120;
}
