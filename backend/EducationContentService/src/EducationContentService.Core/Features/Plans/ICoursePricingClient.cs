namespace EducationContentService.Core.Features.Plans;

/// <summary>
///     Read-only client over AccessService's <c>POST /internal/access/plans/by-course-ids</c>.
///     Resolves active+public+non-archived COURSE-tier plan pricing for a batch of course ids.
///     Courses without a matching plan (or with NULL <c>PriceCents</c>) are absent from the result.
/// </summary>
/// <remarks>
///     Narrow ECS-local client — disambiguated from the unrelated
///     <c>AccessService.Contracts.HttpCommunication.IAccessServiceClient</c> (the full
///     service contract used elsewhere). This one covers only catalog pricing enrichment.
/// </remarks>
public interface ICoursePricingClient
{
    Task<Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error>> GetPlansForCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken ct);
}
