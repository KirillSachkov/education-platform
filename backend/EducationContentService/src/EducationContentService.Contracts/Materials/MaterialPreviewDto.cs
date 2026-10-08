namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Лёгкая «обёрточная» информация о PUBLISHED-материале для OG/Twitter
///     превью-карточек. Возвращается анонимно регардлесс entitlement-чека —
///     заголовок и обложка считаются promotional metadata (по аналогии с
///     `collection.detail` partial-access моделью).
/// </summary>
public sealed record MaterialPreviewDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string Kind,
    string AccessType,
    Guid? ImageId,
    string? ImageUrl,
    DateTime? PublishedAt,
    DateTime UpdatedAt);
