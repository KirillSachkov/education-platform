namespace EducationContentService.Contracts.Collections;

/// <summary>
///     Bulk-смена уровня доступа у всех материалов, лежащих в подборке (рекурсивно по
///     всем секциям). Подборка как сущность не трогается — меняется только
///     <c>Material.AccessType</c>. Материалы могут лежать в других курсах/подборках —
///     смена access распространится на них тоже (предупреждение даёт UI).
/// </summary>
public sealed record BulkSetItemsAccessTypeRequest(string AccessType);
