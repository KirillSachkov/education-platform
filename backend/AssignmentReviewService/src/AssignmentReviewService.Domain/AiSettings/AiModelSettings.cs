namespace AssignmentReviewService.Domain.AiSettings;

/// <summary>
///     Singleton-row, хранящий effective AI-настройки для <see cref="Reviewer"/>
///     slot'а (LLM-ревью). PK — фиксированный <see cref="SINGLETON_ID"/>; если row
///     отсутствует, resolver возвращает дефолты из appsettings.
/// </summary>
public sealed class AiModelSettings
{
    public static readonly Guid SINGLETON_ID = Guid.Parse("00000000-0000-0000-0000-00000000a2c0");

    /// <summary>Максимальная длина admin-редактируемого base review prompt'а.</summary>
    public const int MAX_REVIEWER_BASE_PROMPT_LENGTH = 4000;

    private AiModelSettings() { }

    private AiModelSettings(AiModelSlot reviewer, Guid? updatedByUserId)
    {
        Id = SINGLETON_ID;
        Reviewer = reviewer;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = NormalizeUserId(updatedByUserId);
    }

    public Guid Id { get; private set; }

    public AiModelSlot Reviewer { get; private set; } = null!;

    /// <summary>
    ///     Admin-редактируемый глобальный base review prompt (#15). Передаётся
    ///     reviewer'у как TRUSTED instructions block сразу после system prompt'а.
    ///     <c>null</c> → resolver падает на config-дефолт.
    /// </summary>
    public string? ReviewerBasePrompt { get; private set; }

    /// <summary>
    ///     Платформенный тумблер AI-проверки PR (#355). DB-override над config'ом
    ///     <c>AssignmentReviewAI:ReviewEnabled</c>. Когда <c>false</c> — AI-ревью
    ///     дормант: submission'ы не создают AiReview, авто-ран не запускается.
    /// </summary>
    public bool ReviewEnabled { get; private set; }

    /// <summary>
    ///     Тумблер дозапроса файлов репозитория ревьюером (#798). DB-override над
    ///     config'ом <c>AssignmentReviewAI:RepoContext:Enabled</c>. Когда <c>false</c>
    ///     (default) — ревью идёт старым одношотным путём, без карты репо и need_files.
    /// </summary>
    public bool RepoContextEnabled { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     UUID администратора, последним изменившего настройки. <c>null</c> —
    ///     когда правка пришла от сервисного principal'а (MCP / Hermes через
    ///     client_credentials-токен, у которого нет user-claim'а, т.е.
    ///     <c>UserScopedData.UserId == Guid.Empty</c>). Audit-поле — пустой/нулевой
    ///     id нормализуется в <c>null</c>, а не отвергается, чтобы service-token
    ///     мог менять глобальные AI-настройки.
    /// </summary>
    public Guid? UpdatedByUserId { get; private set; }

    public static Result<AiModelSettings, Error> Create(
        AiModelSlot reviewer,
        Guid? updatedByUserId,
        string? reviewerBasePrompt = null,
        bool reviewEnabled = false,
        bool repoContextEnabled = false)
    {
        if (reviewer is null)
            return GeneralErrors.ValueIsRequired(nameof(reviewer));

        UnitResult<Error> promptCheck = ValidateBasePrompt(reviewerBasePrompt);
        if (promptCheck.IsFailure)
            return promptCheck.Error;

        AiModelSettings settings = new(reviewer, updatedByUserId);
        settings.SetReviewerBasePromptInternal(reviewerBasePrompt);
        settings.ReviewEnabled = reviewEnabled;
        settings.RepoContextEnabled = repoContextEnabled;
        return settings;
    }

    public UnitResult<Error> UpdateAll(
        AiModelSlot reviewer,
        Guid? updatedByUserId,
        string? reviewerBasePrompt = null,
        bool reviewEnabled = false,
        bool repoContextEnabled = false)
    {
        if (reviewer is null)
            return GeneralErrors.ValueIsRequired(nameof(reviewer));

        UnitResult<Error> promptCheck = ValidateBasePrompt(reviewerBasePrompt);
        if (promptCheck.IsFailure)
            return promptCheck.Error;

        Reviewer = reviewer;
        SetReviewerBasePromptInternal(reviewerBasePrompt);
        ReviewEnabled = reviewEnabled;
        RepoContextEnabled = repoContextEnabled;
        UpdatedByUserId = NormalizeUserId(updatedByUserId);
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Сервисные principal'ы (client_credentials) приходят с пустым user-id.
    ///     Нормализуем пустой/нулевой GUID в <c>null</c>, чтобы audit-поле не
    ///     хранило бессмысленный <see cref="Guid.Empty"/>.
    /// </summary>
    private static Guid? NormalizeUserId(Guid? updatedByUserId) =>
        updatedByUserId is null || updatedByUserId.Value == Guid.Empty ? null : updatedByUserId;

    private static UnitResult<Error> ValidateBasePrompt(string? reviewerBasePrompt)
    {
        if (reviewerBasePrompt is not null && reviewerBasePrompt.Length > MAX_REVIEWER_BASE_PROMPT_LENGTH)
            return Error.Validation(
                "ai_settings.base_prompt.too_long",
                $"Базовый промпт ревьюера не может превышать {MAX_REVIEWER_BASE_PROMPT_LENGTH} символов.");
        return UnitResult.Success<Error>();
    }

    private void SetReviewerBasePromptInternal(string? reviewerBasePrompt) =>
        ReviewerBasePrompt = string.IsNullOrWhiteSpace(reviewerBasePrompt)
            ? null
            : reviewerBasePrompt.Trim();
}
