using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Digest;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Modules;
using EducationContentService.Contracts.Ownership;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Contracts.Projects;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Contracts.SearchLookup;
using SharedKernel;

namespace AccessService.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory replacement for <see cref="IEducationContentServiceClient"/> used by
/// AccessService integration tests. Only the methods used by AccessService handlers
/// are implemented; the rest throw <see cref="NotImplementedException"/> to fail
/// loud if a future use-case starts calling them without a fake-side hookup.
///
/// Currently used: <see cref="GetCourseTitlesAsync"/> (PublicPlan course-titles enrichment),
/// <see cref="GetMaterialSummariesAsync"/> (home-pins enrichment, epic #397).
/// </summary>
public sealed class FakeEducationContentServiceClient : IEducationContentServiceClient
{
    public Dictionary<Guid, string> CourseTitlesById { get; } = [];

    /// <summary>
    /// Material summaries returned by <see cref="GetMaterialSummariesAsync"/> (home-pins
    /// enrichment, epic #397). Keyed by material id; only requested ids present in the map
    /// are returned (a material absent from the map ⇒ "not found in ECS" → 404 in AddHomePin
    /// or filtered out in me/home-pins). Set <see cref="MaterialSummariesFailure"/> to simulate
    /// an ECS outage. Reset via <see cref="ResetMaterialSummaries"/>.
    /// </summary>
    public Dictionary<Guid, MaterialSummaryDto> MaterialSummariesById { get; } = [];

    public Error? MaterialSummariesFailure { get; set; }

    /// <summary>
    /// Platform-wide course id list. Used by <see cref="GetAllCourseIdsAsync"/> for global
    /// FULL_ALL / LEARN_ALL covered-courses expansion.
    /// </summary>
    public IReadOnlyList<Guid> AllCourseIds { get; set; } = [];

    public Error? AllCourseIdsFailure { get; set; }

    /// <summary>
    /// Per-author course id list. Used by <see cref="GetAuthorCourseIdsAsync"/> for
    /// author-filtered and legacy FREE derive read-models. Authors absent from
    /// the map resolve to an empty list (author with no courses). Set
    /// <see cref="AuthorCourseIdsFailure"/> to simulate an ECS outage.
    /// </summary>
    public Dictionary<Guid, IReadOnlyList<Guid>> AuthorCourseIds { get; } = [];

    public Error? AuthorCourseIdsFailure { get; set; }

    /// <summary>
    /// Course → author map for <see cref="GetCourseLookupAsync"/>. Used by the grantees
    /// read-model to resolve the authoritative authorId server-side (defense-in-depth against
    /// a caller-supplied authorId). Unknown courses resolve to a NotFound error.
    /// </summary>
    public Dictionary<Guid, Guid> CourseAuthorsById { get; } = [];

    public Task<Result<IReadOnlyList<CourseTitleDto>, Error>> GetCourseTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CourseTitleDto> rows = ids
            .Select(id => CourseTitlesById.TryGetValue(id, out string? title)
                ? new CourseTitleDto(id, title)
                : null)
            .OfType<CourseTitleDto>()
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<CourseTitleDto>, Error>(rows));
    }

    // ── Unused — guarded by NotImplementedException ────────────────────

    public Task<Result<CourseDetailDto, Error>> GetCourseDetailAsync(Guid courseId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ModuleDetailDto, Error>> GetModuleDetailAsync(Guid moduleId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ProjectDetailDto, Error>> GetProjectDetailAsync(Guid projectId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IssueDetailDto, Error>> GetDetailIssueByIdAsync(Guid issueId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<CourseSearchLookupDto, Error>> GetCourseSearchLookupAsync(Guid courseId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ModuleSearchLookupDto, Error>> GetModuleSearchLookupAsync(Guid moduleId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ProjectSearchLookupDto, Error>> GetProjectSearchLookupAsync(Guid projectId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<MaterialSearchLookupDto, Error>> GetMaterialSearchLookupAsync(Guid materialId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IssueSearchLookupDto, Error>> GetIssueSearchLookupAsync(Guid issueId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<CollectionSearchLookupDto, Error>> GetCollectionSearchLookupAsync(Guid collectionId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<CourseDto, Error>> GetCourseLookupAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (CourseAuthorsById.TryGetValue(courseId, out Guid authorId))
        {
            return Task.FromResult(Result.Success<CourseDto, Error>(
                new CourseDto(courseId, authorId, "PUBLISHED", HasFreeContent: false)));
        }

        return Task.FromResult(Result.Failure<CourseDto, Error>(
            Error.NotFound("course.not.found", "Курс не найден")));
    }

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAllCourseIdsAsync(CancellationToken cancellationToken)
    {
        if (AllCourseIdsFailure is { } error)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<Guid>, Error>(error));
        }

        return Task.FromResult(Result.Success<IReadOnlyList<Guid>, Error>(AllCourseIds));
    }

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAuthorCourseIdsAsync(Guid authorId, CancellationToken cancellationToken)
    {
        if (AuthorCourseIdsFailure is { } error)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<Guid>, Error>(error));
        }

        IReadOnlyList<Guid> courses = AuthorCourseIds.TryGetValue(authorId, out IReadOnlyList<Guid>? ids)
            ? ids
            : [];
        return Task.FromResult(Result.Success<IReadOnlyList<Guid>, Error>(courses));
    }

    public Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> GetCourseProgressBlueprintsAsync(GetCourseProgressBlueprintsRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> ResolveMaterialTargetsAsync(ResolveMaterialTargetsRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ModuleDto, Error>> GetModuleLookupAsync(Guid courseId, Guid moduleId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<ProjectDto, Error>> GetProjectLookupAsync(Guid courseId, Guid projectId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<MaterialDto, Error>> GetMaterialLookupAsync(Guid moduleId, Guid materialId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<UnitResult<Error>> UpdateMaterialContentAsync(Guid materialId, UpdateMaterialContentRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<UnitResult<Error>> UpdateVideoChaptersAsync(Guid videoId, UpdateVideoChaptersRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> GetMaterialCourseContextsAsync(Guid materialId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IssueDto, Error>> GetIssueLookupAsync(Guid projectId, Guid issueId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<EntityOwnershipDto, Error>> GetEntityOwnershipAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> GetMaterialTitlesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> GetMaterialSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (MaterialSummariesFailure is { } error)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<MaterialSummaryDto>, Error>(error));
        }

        IReadOnlyList<MaterialSummaryDto> rows = ids
            .Select(id => MaterialSummariesById.TryGetValue(id, out MaterialSummaryDto? summary)
                ? summary
                : null)
            .OfType<MaterialSummaryDto>()
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<MaterialSummaryDto>, Error>(rows));
    }

    /// <summary>Clears configured material summaries + outage flag (called in per-test reset).</summary>
    public void ResetMaterialSummaries()
    {
        MaterialSummariesById.Clear();
        MaterialSummariesFailure = null;
    }

    /// <summary>Clears course lookup state used by access derive tests.</summary>
    public void ResetCourseLookups()
    {
        AllCourseIds = [];
        AllCourseIdsFailure = null;
        AuthorCourseIds.Clear();
        AuthorCourseIdsFailure = null;
        CourseAuthorsById.Clear();
    }

    /// <summary>
    /// Registers a published material summary so home-pins enrichment can find it. Minimal
    /// helper — only the fields the home-pins handlers read (Title/Kind/AccessType/Status/
    /// ThumbnailUrl) matter; the rest are filled with inert defaults.
    /// </summary>
    public void AddMaterialSummary(
        Guid id,
        string title = "Material",
        string kind = "ARTICLE",
        string accessType = "PUBLIC",
        string status = "PUBLISHED",
        string? thumbnailUrl = null,
        Guid? authorId = null)
    {
        MaterialSummariesById[id] = new MaterialSummaryDto(
            Id: id,
            AuthorId: authorId ?? Guid.Empty,
            Title: title,
            Preview: null,
            Kind: kind,
            Status: status,
            AccessType: accessType,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            PublishedAt: status == "PUBLISHED" ? DateTime.UtcNow : null,
            ImageId: null,
            VideoId: null)
        {
            ThumbnailUrl = thumbnailUrl,
        };
    }

    public Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> GetMaterialCourseBindingsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> GetIssueCourseBindingsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> GetProjectTitlesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<IssueTitleDto>, Error>> GetIssueTitlesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<QuizAnswerKeyDto, Error>> GetQuizAnswerKeyAsync(Guid quizId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> GetQuizModuleLookupAsync(Guid quizId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> GetQuizSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Result<DigestContentDto, Error>> GetDigestContentAsync(DateTime sinceUtc, int maxItemsPerKind, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
