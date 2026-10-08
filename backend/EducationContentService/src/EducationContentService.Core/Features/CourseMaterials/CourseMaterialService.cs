using EducationContentService.Domain.Courses;
using Ordering;

namespace EducationContentService.Core.Features.CourseMaterials;

public sealed class CourseMaterialService
{
    private readonly ICourseMaterialsRepository _courseMaterialsRepository;
    private readonly ILogger<CourseMaterialService> _logger;

    public CourseMaterialService(
        ICourseMaterialsRepository courseMaterialsRepository,
        ILogger<CourseMaterialService> logger)
    {
        _courseMaterialsRepository = courseMaterialsRepository;
        _logger = logger;
    }

    public async Task<Result<CourseMaterial, Error>> CreateAsync(
        Guid courseId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        Result<string, Error> sortKeyResult = await ComputeAppendSortKey(courseId, cancellationToken);
        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        SortKey sortKey = SortKey.Create(sortKeyResult.Value).Value;
        var item = new CourseMaterial(courseId, materialId, sortKey);

        await _courseMaterialsRepository.AddAsync(item, cancellationToken);

        _logger.LogInformation(
            "Material {MaterialId} bound to course {CourseId}",
            materialId, courseId);

        return item;
    }

    public async Task<Result<SortKey, Error>> ComputeMoveSortKey(
        Guid courseId,
        Guid materialId,
        string? afterSortKey,
        string? beforeSortKey,
        CancellationToken cancellationToken)
    {
        SortKey? after = afterSortKey != null ? SortKey.Create(afterSortKey).Value : null;
        SortKey? before = beforeSortKey != null ? SortKey.Create(beforeSortKey).Value : null;

        if (after == null && before == null)
        {
            Result<CourseMaterial, Error> firstResult = await _courseMaterialsRepository.GetByAsync(
                i => i.CourseId == courseId && i.MaterialId != materialId,
                i => i.SortKey,
                descending: false,
                cancellationToken);

            if (firstResult.IsSuccess)
            {
                before = SortKey.Create(firstResult.Value.SortKey.Value).Value;
            }

            return before != null
                ? SortKey.Before(before)
                : SortKey.Initial();
        }

        if (after != null && before == null)
            return SortKey.After(after);

        if (after == null)
            return SortKey.Before(before!);

        return SortKey.Between(before, after);
    }

    private async Task<Result<string, Error>> ComputeAppendSortKey(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        Result<CourseMaterial, Error> lastItemResult = await _courseMaterialsRepository.GetByAsync(
            i => i.CourseId == courseId,
            i => i.SortKey,
            descending: true,
            cancellationToken);

        string sortKey = lastItemResult.IsFailure
            ? SortKey.Initial().Value
            : SortKey.After(SortKey.Create(lastItemResult.Value.SortKey.Value).Value).Value;

        return sortKey;
    }
}
