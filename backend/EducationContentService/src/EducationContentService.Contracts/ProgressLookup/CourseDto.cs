namespace EducationContentService.Contracts.ProgressLookup;

/// <summary>
///     Легковесный DTO курса для сервиса прогресса (service-to-service).
/// </summary>
/// <param name="HasFreeContent">
///     true — у курса есть хотя бы один опубликованный урок или задание с AccessType=FREE
///     (т.е. возможна пробная самозапись).
/// </param>
public sealed record CourseDto(
    Guid CourseId,
    Guid AuthorId,
    string Status,
    bool HasFreeContent);
