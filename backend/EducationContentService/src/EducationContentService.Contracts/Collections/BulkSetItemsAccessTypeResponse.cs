namespace EducationContentService.Contracts.Collections;

/// <summary>
///     Итог bulk-операции:
///     <c>UpdatedCount</c> — сколько материалов реально сменили AccessType (publish'нули event);
///     <c>SkippedCount</c> — у скольких AccessType уже был запрошенным;
///     <c>SkippedNotOwnedCount</c> — сколько материалов в подборке принадлежит другому автору и
///     было пропущено (актуально для cross-author подборок);
///     <c>TotalCount</c> — общее число уникальных материалов в подборке (Updated + Skipped + SkippedNotOwned).
/// </summary>
public sealed record BulkSetItemsAccessTypeResponse(
    int UpdatedCount,
    int SkippedCount,
    int SkippedNotOwnedCount,
    int TotalCount);
