namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Минимальный батч-проекшн проекта: id + title. Используется ProgressService'ом
///     для enrichment'а review-feed'а (отображение названия проекта вместо GUID).
/// </summary>
public sealed record ProjectTitleDto(Guid ProjectId, string Title);

public sealed record GetProjectTitlesRequest(IReadOnlyCollection<Guid> Ids);
