namespace AssignmentReviewService.Contracts.AiSettings;

public sealed record AssignmentReviewAiModelSettingsDto(
    AiModelSlotEffectiveDto Reviewer,
    ReviewerBasePromptEffectiveDto ReviewerBasePrompt,
    /// <summary>Эффективный тумблер AI-проверки PR (#355): DB-override или config-дефолт (false).</summary>
    bool ReviewEnabled,
    /// <summary>Тумблер дозапроса файлов репозитория ревьюером (#798): DB-override или config-дефолт (false).</summary>
    bool RepoContextEnabled,
    DateTime? UpdatedAt,
    Guid? UpdatedByUserId);

public sealed record ReviewerBasePromptEffectiveDto(
    string Value,
    /// <summary>"CONFIG" | "DATABASE" — origin of the effective base prompt.</summary>
    string Source);

public sealed record AiModelSlotEffectiveDto(
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds,
    /// <summary>"CONFIG" | "DATABASE" — origin of effective values.</summary>
    string Source);

public sealed record UpdateAiModelSettingsRequest(
    AiModelSlotInputDto Reviewer,
    /// <summary>
    ///     Глобальный base review prompt (#15). null / пусто → сбрасывает на config-дефолт.
    /// </summary>
    string? ReviewerBasePrompt = null,
    /// <summary>Тумблер AI-проверки PR (#355). Фронт всегда шлёт текущее значение из GET.</summary>
    bool ReviewEnabled = false,
    /// <summary>Тумблер дозапроса файлов репозитория (#798). Фронт всегда шлёт текущее значение из GET.</summary>
    bool RepoContextEnabled = false);

public sealed record AiModelSlotInputDto(
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds);
