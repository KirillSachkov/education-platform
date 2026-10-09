namespace AccessService.Domain;

/// <summary>
/// Bitmask: какие возможности открывает план. Автор задаёт при создании плана,
/// pricing-страница рендерит как ✓/✗ список. Backend access checks: ViewMaterials
/// форсится для любого ACTIVE-grant'а; SubmitIssues, CommunityAccess проверяются
/// в соответствующих эндпоинтах. Capabilities — *поверх* resource-tag access:
/// сначала grant даёт *доступ* к материалу через Redis SINTER, потом capability
/// проверяется на *действие* (отправить решение / попасть в чат / получить ревью).
/// </summary>
[Flags]
public enum PlanCapabilities
{
#pragma warning disable S2346 // Suppression for [Flags] enum: NONE is the conventional sentinel name for "no flags set" in .NET.
    NONE = 0,
#pragma warning restore S2346

    /// <summary>Просмотр уроков — статьи, видео, заметки. Базовая возможность любого плана.</summary>
    VIEW_MATERIALS = 1 << 0,

    /// <summary>Отправлять решения заданий на проверку.</summary>
    SUBMIT_ISSUES = 1 << 1,

    /// <summary>Получать code review от автора (требует SubmitIssues).</summary>
    CODE_REVIEW = 1 << 2,

    /// <summary>Доступ в закрытое сообщество (Telegram-чат).</summary>
    COMMUNITY_ACCESS = 1 << 3,

    /// <summary>Доступ к еженедельным созвонам + архиву записей.</summary>
    LIVE_CALLS = 1 << 4,

    /// <summary>Помощь с трудоустройством — резюме, mock-собеседования.</summary>
    JOB_SUPPORT = 1 << 5,

    /// <summary>Historical stored flag. New mutations reject it and entitlement projection masks it.</summary>
    TRAINER_PRO = 1 << 6,

    /// <summary>Полный набор возможностей (default для платных планов). Не включает
    /// <see cref="TRAINER_PRO"/> — это отдельный подписочный add-on (#614).</summary>
    FULL = VIEW_MATERIALS | SUBMIT_ISSUES | CODE_REVIEW | COMMUNITY_ACCESS | LIVE_CALLS | JOB_SUPPORT,

    /// <summary>Только просмотр материалов (для подписки «учусь сам»).</summary>
    LEARN_ONLY = VIEW_MATERIALS,
}