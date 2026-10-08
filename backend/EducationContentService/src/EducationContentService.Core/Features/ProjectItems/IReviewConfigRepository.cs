using System.Linq.Expressions;
using EducationContentService.Domain.Projects;

namespace EducationContentService.Core.Features.ProjectItems;

/// <summary>
///     Repository for AI-review configuration aggregates (Phase 5 / issue #15).
///     <see cref="ProjectReviewContext"/> и <see cref="ReviewSpec"/> объединены в один
///     repository — оба простых aggregate без bulk-операций; разделять на 2 repo overhead.
/// </summary>
public interface IReviewConfigRepository
{
    Task AddProjectReviewContextAsync(ProjectReviewContext context, CancellationToken ct = default);

    Task<ProjectReviewContext?> GetProjectReviewContextAsync(
        Expression<Func<ProjectReviewContext, bool>> predicate,
        CancellationToken ct = default);

    Task AddReviewSpecAsync(ReviewSpec spec, CancellationToken ct = default);

    Task<ReviewSpec?> GetReviewSpecAsync(
        Expression<Func<ReviewSpec, bool>> predicate,
        CancellationToken ct = default);
}
