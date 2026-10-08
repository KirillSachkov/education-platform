namespace EducationContentService.Contracts.Issues;

/// <summary>
///     Минимальный батч-проекшн задачи: id + title. Используется ProgressService'ом
///     для enrichment'а review-feed'а (отображение названия задачи вместо GUID).
/// </summary>
public sealed record IssueTitleDto(Guid IssueId, string Title);

public sealed record GetIssueTitlesRequest(IReadOnlyCollection<Guid> Ids);
