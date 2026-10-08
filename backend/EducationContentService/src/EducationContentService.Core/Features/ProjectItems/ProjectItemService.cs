using EducationContentService.Domain.Projects;
using Ordering;

namespace EducationContentService.Core.Features.ProjectItems;

public sealed class ProjectItemService
{
    private readonly IProjectItemsRepository _projectItemsRepository;
    private readonly OrderingService<ProjectItem> _ordering;
    private readonly ILogger<ProjectItemService> _logger;

    public ProjectItemService(
        IProjectItemsRepository projectItemsRepository,
        OrderingService<ProjectItem> ordering,
        ILogger<ProjectItemService> logger)
    {
        _projectItemsRepository = projectItemsRepository;
        _ordering = ordering;
        _logger = logger;
    }

    /// <summary>
    ///     Creates a new <see cref="ProjectItem" /> with the correct SortKey (appended to the end),
    ///     adds it to the repository and returns the created entity.
    /// </summary>
    public async Task<Result<ProjectItem, Error>> CreateAsync(
        Guid projectId,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        SortKey sortKey = await _ordering.ComputeAppendSortKeyAsync(
            i => i.ProjectId == projectId, cancellationToken);

        var item = new ProjectItem(projectId, issueId, sortKey, isOptional: false, maxScore: null);

        await _projectItemsRepository.AddAsync(item, cancellationToken);

        _logger.LogInformation(
            "Issue {IssueId} bound to project {ProjectId}",
            issueId, projectId);

        return item;
    }

    /// <summary>
    ///     Computes a new SortKey to place item between neighbors (for move operations).
    /// </summary>
    public async Task<Result<SortKey, Error>> ComputeMoveSortKey(
        Guid projectId,
        Guid issueId,
        string? afterSortKey,
        string? beforeSortKey,
        CancellationToken cancellationToken)
    {
        return await _ordering.ComputeMoveSortKeyAsync(
            i => i.ProjectId == projectId,
            afterSortKey, beforeSortKey, cancellationToken);
    }
}
