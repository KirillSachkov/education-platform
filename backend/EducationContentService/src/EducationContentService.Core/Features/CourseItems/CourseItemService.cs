using EducationContentService.Domain.Courses;
using Ordering;

namespace EducationContentService.Core.Features.CourseItems;

public sealed class CourseItemService
{
    private readonly ICourseItemsRepository _courseItemsRepository;
    private readonly OrderingService<CourseItem> _ordering;
    private readonly ILogger<CourseItemService> _logger;

    public CourseItemService(
        ICourseItemsRepository courseItemsRepository,
        OrderingService<CourseItem> ordering,
        ILogger<CourseItemService> logger)
    {
        _courseItemsRepository = courseItemsRepository;
        _ordering = ordering;
        _logger = logger;
    }

    /// <summary>
    ///     Creates a new <see cref="CourseItem" /> with the correct SortKey (appended to the end),
    ///     adds it to the repository and returns the created entity.
    /// </summary>
    public async Task<Result<CourseItem, Error>> CreateAsync(
        Guid courseId,
        CourseItemType itemType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        SortKey sortKey = await _ordering.ComputeAppendSortKeyAsync(
            i => i.CourseId == courseId, cancellationToken);

        var item = new CourseItem(courseId, itemType, referenceId, sortKey, isOptional: false);

        await _courseItemsRepository.AddAsync(item, cancellationToken);

        _logger.LogInformation(
            "Item {ReferenceId} ({ItemType}) bound to course {CourseId}",
            referenceId, itemType, courseId);

        return item;
    }

    /// <summary>
    ///     Computes a new SortKey to place item between neighbors (for move operations).
    /// </summary>
    public async Task<Result<SortKey, Error>> ComputeMoveSortKey(
        Guid courseId,
        Guid referenceId,
        string? afterSortKey,
        string? beforeSortKey,
        CancellationToken cancellationToken)
    {
        return await _ordering.ComputeMoveSortKeyAsync(
            i => i.CourseId == courseId,
            afterSortKey, beforeSortKey, cancellationToken);
    }
}
