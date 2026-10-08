namespace EducationContentService.Contracts.Collections;

/// <summary>
///     Запрос на обновление подборки.
/// </summary>
/// <param name="CoverId">
///     Желаемое состояние обложки. <c>null</c> ⇒ открепить (sync detach).
///     Значение ⇒ привязать (idempotent через sync FileService bind).
/// </param>
public sealed record UpdateCollectionRequest(
    string Title,
    string? Description,
    string AccessType,
    Guid? CoverId = null);
