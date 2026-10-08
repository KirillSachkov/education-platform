namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Лёгкая мета карточки материала: просмотры + длительность видео + авторский кредит
///     (имя/аватар). Используется поверхностями, которые рендерят карточки не из ECS-фидов
///     (база знаний питается search-документами Typesense — в них нет views/duration/author).
///     Issue #500; авторский кредит — #569 (model A co-author). Имя автора — публичная
///     атрибуция (byline), не gated-контент; тело/preview по-прежнему не отдаются.
/// </summary>
public sealed record MaterialCardMetaDto(
    Guid MaterialId,
    long ViewsCount,
    double? DurationSeconds,
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null);
