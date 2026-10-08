namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Минимальный батч-проекшн материала: id + title.
///     Используется CommentService author-feed'ом для enrichment'а (отображение
///     заголовка материала, под которым оставлен коммент) — без round-trip'ов
///     на полный <see cref="MaterialDetailDto"/>.
/// </summary>
public sealed record MaterialTitleDto(Guid MaterialId, string Title);
