using Core.HttpCommunication;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Digest;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Modules;
using EducationContentService.Contracts.Ownership;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Contracts.Projects;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;

namespace EducationContentService.Contracts.HttpCommunication;

internal sealed class EducationContentServiceClient : BaseHttpClient, IEducationContentServiceClient
{
    private const string SERVICE_NAME = "EducationContentService";
    private const int MAX_PROGRESS_BLUEPRINT_BATCH_SIZE = 200;

    public EducationContentServiceClient(
        HttpClient httpClient,
        ILogger<EducationContentServiceClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<CourseDetailDto, Error>> GetCourseDetailAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => GetAsync<CourseDetailDto>($"/courses/{courseId}/detail", cancellationToken);

    public Task<Result<ModuleDetailDto, Error>> GetModuleDetailAsync(
        Guid moduleId,
        CancellationToken cancellationToken)
        => GetAsync<ModuleDetailDto>($"/modules/{moduleId}/detail", cancellationToken);

    public Task<Result<ProjectDetailDto, Error>> GetProjectDetailAsync(
        Guid projectId,
        CancellationToken cancellationToken)
        => GetAsync<ProjectDetailDto>($"/projects/{projectId}/detail", cancellationToken);

    public Task<Result<IssueDetailDto, Error>> GetDetailIssueByIdAsync(
        Guid issueId,
        CancellationToken cancellationToken)
        => GetAsync<IssueDetailDto>($"/issues/{issueId}/detail", cancellationToken);

    public Task<Result<CourseSearchLookupDto, Error>> GetCourseSearchLookupAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => GetAsync<CourseSearchLookupDto>($"/internal/search/courses/{courseId}", cancellationToken);

    public Task<Result<ModuleSearchLookupDto, Error>> GetModuleSearchLookupAsync(
        Guid moduleId,
        CancellationToken cancellationToken)
        => GetAsync<ModuleSearchLookupDto>($"/internal/search/modules/{moduleId}", cancellationToken);

    public Task<Result<ProjectSearchLookupDto, Error>> GetProjectSearchLookupAsync(
        Guid projectId,
        CancellationToken cancellationToken)
        => GetAsync<ProjectSearchLookupDto>($"/internal/search/projects/{projectId}", cancellationToken);

    public Task<Result<MaterialSearchLookupDto, Error>> GetMaterialSearchLookupAsync(
        Guid materialId,
        CancellationToken cancellationToken)
        => GetAsync<MaterialSearchLookupDto>($"/internal/search/materials/{materialId}", cancellationToken);

    public Task<Result<IssueSearchLookupDto, Error>> GetIssueSearchLookupAsync(
        Guid issueId,
        CancellationToken cancellationToken)
        => GetAsync<IssueSearchLookupDto>($"/internal/search/issues/{issueId}", cancellationToken);

    public Task<Result<CollectionSearchLookupDto, Error>> GetCollectionSearchLookupAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
        => GetAsync<CollectionSearchLookupDto>($"/internal/search/collections/{collectionId}", cancellationToken);

    // Progress lookup contracts (service-to-service)
    public Task<Result<CourseDto, Error>> GetCourseLookupAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => GetAsync<CourseDto>($"/internal/progress/courses/{courseId}", cancellationToken);

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAllCourseIdsAsync(
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<Guid>>("/internal/progress/courses/ids", cancellationToken);

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAuthorCourseIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<Guid>>(
            $"/internal/progress/courses/by-author/{authorId}/ids", cancellationToken);

    public async Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> GetCourseProgressBlueprintsAsync(
        GetCourseProgressBlueprintsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CourseIds.Count <= MAX_PROGRESS_BLUEPRINT_BATCH_SIZE)
        {
            return await PostAsync<GetCourseProgressBlueprintsRequest, IReadOnlyCollection<CourseProgressBlueprintDto>>(
                "/internal/progress/courses/blueprints",
                request,
                cancellationToken);
        }

        var result = new List<CourseProgressBlueprintDto>(request.CourseIds.Count);
        foreach (Guid[] chunk in request.CourseIds.Chunk(MAX_PROGRESS_BLUEPRINT_BATCH_SIZE))
        {
            Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> chunkResult =
                await PostAsync<GetCourseProgressBlueprintsRequest, IReadOnlyCollection<CourseProgressBlueprintDto>>(
                    "/internal/progress/courses/blueprints",
                    new GetCourseProgressBlueprintsRequest(chunk),
                    cancellationToken);
            if (chunkResult.IsFailure)
                return chunkResult.Error;

            result.AddRange(chunkResult.Value);
        }

        return result;
    }

    public Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> ResolveMaterialTargetsAsync(
        ResolveMaterialTargetsRequest request,
        CancellationToken cancellationToken)
        => PostAsync<ResolveMaterialTargetsRequest, IReadOnlyCollection<ResolvedMaterialDto>>(
            "/internal/progress/materials/resolve",
            request,
            cancellationToken);

    public Task<Result<ModuleDto, Error>> GetModuleLookupAsync(
        Guid courseId,
        Guid moduleId,
        CancellationToken cancellationToken)
        => GetAsync<ModuleDto>($"/internal/progress/courses/{courseId}/modules/{moduleId}", cancellationToken);

    public Task<Result<ProjectDto, Error>> GetProjectLookupAsync(
        Guid courseId,
        Guid projectId,
        CancellationToken cancellationToken)
        => GetAsync<ProjectDto>($"/internal/progress/courses/{courseId}/projects/{projectId}", cancellationToken);

    public Task<Result<MaterialDto, Error>> GetMaterialLookupAsync(
        Guid moduleId,
        Guid materialId,
        CancellationToken cancellationToken)
        => GetAsync<MaterialDto>($"/internal/progress/modules/{moduleId}/materials/{materialId}", cancellationToken);

    public async Task<UnitResult<Error>> UpdateMaterialContentAsync(
        Guid materialId,
        UpdateMaterialContentRequest request,
        CancellationToken cancellationToken)
    {
        Result<Guid, Error> result = await PutAsync<UpdateMaterialContentRequest, Guid>(
            $"/internal/materials/{materialId}/content/",
            request,
            cancellationToken);

        return result.IsSuccess
            ? UnitResult.Success<Error>()
            : result.Error;
    }

    public async Task<UnitResult<Error>> UpdateVideoChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken)
    {
        Result<int, Error> result = await PutAsync<UpdateVideoChaptersRequest, int>(
            $"/internal/videos/{videoId}/chapters/",
            request,
            cancellationToken);

        return result.IsSuccess
            ? UnitResult.Success<Error>()
            : result.Error;
    }

    public Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> GetMaterialCourseContextsAsync(
        Guid materialId,
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<MaterialCourseContextDto>>(
            $"/internal/progress/materials/{materialId}/course-contexts",
            cancellationToken);

    public Task<Result<IssueDto, Error>> GetIssueLookupAsync(
        Guid projectId,
        Guid issueId,
        CancellationToken cancellationToken)
        => GetAsync<IssueDto>($"/internal/progress/projects/{projectId}/issues/{issueId}", cancellationToken);

    public Task<Result<EntityOwnershipDto, Error>> GetEntityOwnershipAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
        => GetAsync<EntityOwnershipDto>($"/internal/ownership/{entityType}/{entityId}", cancellationToken);

    public Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> GetMaterialTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetMaterialTitlesRequest, IReadOnlyList<MaterialTitleDto>>(
            "/internal/materials/titles",
            new GetMaterialTitlesRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> GetMaterialSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetMaterialSummariesRequest, IReadOnlyList<MaterialSummaryDto>>(
            "/internal/materials/summaries",
            new GetMaterialSummariesRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> GetMaterialCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetMaterialCourseBindingsRequest, IReadOnlyList<MaterialCourseBindingLookupDto>>(
            "/internal/materials/course-bindings",
            new GetMaterialCourseBindingsRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> GetIssueCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetIssueCourseBindingsRequest, IReadOnlyList<IssueCourseBindingLookupDto>>(
            "/internal/issues/course-bindings",
            new GetIssueCourseBindingsRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<CourseTitleDto>, Error>> GetCourseTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetCourseTitlesRequest, IReadOnlyList<CourseTitleDto>>(
            "/internal/courses/titles",
            new GetCourseTitlesRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> GetProjectTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetProjectTitlesRequest, IReadOnlyList<ProjectTitleDto>>(
            "/internal/projects/titles",
            new GetProjectTitlesRequest(ids),
            cancellationToken);

    public Task<Result<IReadOnlyList<IssueTitleDto>, Error>> GetIssueTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetIssueTitlesRequest, IReadOnlyList<IssueTitleDto>>(
            "/internal/issues/titles",
            new GetIssueTitlesRequest(ids),
            cancellationToken);

    public Task<Result<QuizAnswerKeyDto, Error>> GetQuizAnswerKeyAsync(
        Guid quizId,
        CancellationToken cancellationToken)
        => GetAsync<QuizAnswerKeyDto>($"/internal/quizzes/{quizId}/answer-key", cancellationToken);

    public Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> GetQuizModuleLookupAsync(
        Guid quizId,
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<QuizModuleContextDto>>(
            $"/internal/quizzes/{quizId}/module-lookup",
            cancellationToken);

    public Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> GetQuizSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
        => PostAsync<GetQuizSummariesRequest, IReadOnlyList<QuizSummaryLookupDto>>(
            "/internal/quizzes/summaries",
            new GetQuizSummariesRequest(ids),
            cancellationToken);

    public Task<Result<DigestContentDto, Error>> GetDigestContentAsync(
        DateTime sinceUtc,
        int maxItemsPerKind,
        CancellationToken cancellationToken)
        => GetAsync<DigestContentDto>(
            $"/internal/digest/content?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}&maxItems={maxItemsPerKind}",
            cancellationToken);
}
