using Common;
using EducationContentService.Contracts;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Digest;
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

namespace ProgressService.IntegrationTests.Infrastructure;

public sealed class MockEducationContentServiceClient : IEducationContentServiceClient
{
    private readonly Dictionary<Guid, CourseDto> _courses = [];
    private readonly Dictionary<Guid, CourseProgressBlueprintDto> _courseBlueprints = [];
    private readonly Dictionary<ModuleLookupKey, ModuleDto> _modules = [];
    private readonly Dictionary<ProjectLookupKey, ProjectDto> _projects = [];
    private readonly Dictionary<IssueLookupKey, IssueDto> _issues = [];
    private readonly Dictionary<MaterialLookupKey, MaterialDto> _materials = [];
    private readonly Dictionary<Guid, List<MaterialCourseContextDto>> _materialCourseContexts = [];
    private readonly Dictionary<Guid, List<QuizModuleContextDto>> _quizModuleContexts = [];
    private readonly Dictionary<ResolvedMaterialKey, ResolvedMaterialDto> _resolvedMaterials = [];
    private readonly Dictionary<Guid, QuizAnswerKeyDto> _quizAnswerKeys = [];
    private readonly Dictionary<Guid, QuizSummaryLookupDto> _quizSummaries = [];
    private readonly Dictionary<Guid, string> _courseTitles = [];
    private readonly Dictionary<(string EntityType, Guid EntityId), EntityOwnershipDto> _entityOwnerships =
        new(EntityOwnershipKeyComparer.Instance);
    private readonly Dictionary<Guid, IssueCourseBindingLookupDto> _issueCourseBindings = [];
    private bool _resolveMaterialsUnavailable;
    private bool _quizAnswerKeysUnavailable;
    private bool _entityOwnershipUnavailable;

    public void Reset()
    {
        _courses.Clear();
        _courseBlueprints.Clear();
        _modules.Clear();
        _projects.Clear();
        _issues.Clear();
        _materials.Clear();
        _materialCourseContexts.Clear();
        _quizModuleContexts.Clear();
        _resolvedMaterials.Clear();
        _quizAnswerKeys.Clear();
        _quizSummaries.Clear();
        _courseTitles.Clear();
        _entityOwnerships.Clear();
        _issueCourseBindings.Clear();
        _resolveMaterialsUnavailable = false;
        _quizAnswerKeysUnavailable = false;
        _entityOwnershipUnavailable = false;
    }

    /// <summary>
    ///     Регистрирует владельца сущности (#693): mock вернёт его на
    ///     <see cref="GetEntityOwnershipAsync"/>. Незарегистрированный (entityType, id) →
    ///     пустой DTO (как реальный сервис для orphan-сущности).
    /// </summary>
    public void AddEntityOwnership(string entityType, Guid entityId, Guid? courseId, Guid? authorId) =>
        _entityOwnerships[(entityType, entityId)] = new EntityOwnershipDto(courseId, authorId);

    /// <summary>
    ///     Регистрирует primary-привязку задачи к курсу (#693): mock вернёт её на
    ///     <see cref="GetIssueCourseBindingsAsync"/>. Незарегистрированный issueId в ответ
    ///     не попадает (зеркало реального сервиса — задача без привязки к курсу).
    /// </summary>
    public void AddIssueCourseBinding(Guid issueId, Guid courseId, string? courseSlug = null) =>
        _issueCourseBindings[issueId] = new IssueCourseBindingLookupDto(
            issueId, courseId, courseSlug ?? courseId.ToString());

    /// <summary>Симулирует недоступность ECS на entity-ownership lookup (#693 — graceful).</summary>
    public void SetEntityOwnershipUnavailable() => _entityOwnershipUnavailable = true;

    /// <summary>
    ///     Регистрирует заголовок курса, который mock вернёт на
    ///     <see cref="GetCourseTitlesAsync"/> (#556 — админ-статистика по тестам
    ///     группирует строки по курсу). Незарегистрированные ID в ответ не попадают.
    /// </summary>
    public void AddCourseTitle(Guid courseId, string title) =>
        _courseTitles[courseId] = title;

    public void AddQuizAnswerKey(QuizAnswerKeyDto answerKey) =>
        _quizAnswerKeys[answerKey.QuizId] = answerKey;

    /// <summary>
    ///     Регистрирует summary квиза, которое mock вернёт на
    ///     <see cref="GetQuizSummariesAsync"/> (#556). Незарегистрированные ID в ответ
    ///     не попадают — зеркало реального поведения (квиз hard-deleted).
    /// </summary>
    public void AddQuizSummary(Guid quizId, string title, string purpose = "MATERIAL_CHECK", Guid? courseId = null) =>
        _quizSummaries[quizId] = new QuizSummaryLookupDto(quizId, title, purpose, courseId);

    public void SetQuizAnswerKeysUnavailable() => _quizAnswerKeysUnavailable = true;

    public void AddCourse(
        Guid courseId,
        Guid? authorId = null,
        string status = "PUBLISHED",
        bool hasFreeContent = false) =>
        _courses[courseId] = new CourseDto(courseId, authorId ?? Guid.Empty, status, hasFreeContent);

    public void AddCourseBlueprint(
        Guid courseId,
        string title,
        string description,
        Guid? imageId = null,
        int totalModules = 0,
        int totalMaterials = 0,
        int totalUniqueIssues = 0,
        string? imageUrl = null,
        string? courseSlug = null,
        bool isNew = false,
        IReadOnlyList<Guid>? materialIds = null,
        string kind = "COURSE",
        int totalQuizzes = 0,
        IReadOnlyList<Guid>? quizIds = null) =>
        _courseBlueprints[courseId] = new CourseProgressBlueprintDto(
            courseId,
            courseSlug ?? courseId.ToString(),
            title,
            description,
            imageId,
            imageUrl,
            totalModules,
            totalMaterials,
            materialIds ?? [],
            totalUniqueIssues,
            totalQuizzes,
            quizIds ?? [],
            totalMaterials + totalUniqueIssues + totalQuizzes,
            isNew,
            SortKey: "a",
            Kind: kind);

    public void AddModule(Guid courseId, Guid moduleId, int moduleItemsTotal) =>
        _modules[new ModuleLookupKey(courseId, moduleId)] = new ModuleDto(moduleItemsTotal);

    public void AddProject(Guid courseId, Guid projectId, int projectIssuesTotal) =>
        _projects[new ProjectLookupKey(courseId, projectId)] = new ProjectDto(projectIssuesTotal);

    public void AddIssue(
        Guid projectId,
        Guid issueId,
        Guid? moduleId = null,
        string submissionMode = "PULL_REQUEST",
        string? selfCheckInstructions = null,
        bool requiresGithubConnection = true,
        bool requiresReviewApp = true,
        bool isAutoReviewEnabled = true) =>
        _issues[new IssueLookupKey(projectId, issueId)] = new IssueDto(
            moduleId,
            submissionMode,
            selfCheckInstructions,
            requiresGithubConnection,
            requiresReviewApp,
            isAutoReviewEnabled);

    public void AddMaterial(Guid moduleId, Guid materialId, int moduleItemsTotal) =>
        _materials[new MaterialLookupKey(moduleId, materialId)] = new MaterialDto(moduleId, moduleItemsTotal);

    /// <summary>
    ///     Регистрирует для материала пары (courseId, moduleId), которые mock вернёт
    ///     на <see cref="GetMaterialCourseContextsAsync"/>. Несколько вызовов аккумулируются.
    /// </summary>
    public void AddMaterialCourseContext(Guid materialId, Guid courseId, Guid moduleId, int moduleItemsTotal = 1)
    {
        if (!_materialCourseContexts.TryGetValue(materialId, out List<MaterialCourseContextDto>? contexts))
        {
            contexts = [];
            _materialCourseContexts[materialId] = contexts;
        }

        contexts.Add(new MaterialCourseContextDto(courseId, moduleId, moduleItemsTotal));
    }

    /// <summary>
    ///     Регистрирует для квиза пары (courseId, moduleId), которые mock вернёт
    ///     на <see cref="GetQuizModuleLookupAsync"/>. Несколько вызовов аккумулируются.
    /// </summary>
    public void AddQuizModuleContext(Guid quizId, Guid courseId, Guid moduleId, int moduleItemsTotal = 1)
    {
        if (!_quizModuleContexts.TryGetValue(quizId, out List<QuizModuleContextDto>? contexts))
        {
            contexts = [];
            _quizModuleContexts[quizId] = contexts;
        }

        contexts.Add(new QuizModuleContextDto(courseId, moduleId, moduleItemsTotal));
    }

    public void AddResolvedMaterial(
        Guid courseId,
        EntityType entityType,
        Guid entityId,
        string courseTitle,
        string title,
        string? sectionTitle,
        string sectionType,
        string? courseSlug = null)
    {
        _resolvedMaterials[new ResolvedMaterialKey(courseId, entityType, entityId)] = new ResolvedMaterialDto(
            courseId,
            courseSlug ?? courseId.ToString(),
            courseTitle,
            new EntityReferenceDto(entityType, entityId),
            title,
            sectionTitle,
            sectionType);
    }

    public void SetResolveMaterialsUnavailable(bool isUnavailable)
    {
        _resolveMaterialsUnavailable = isUnavailable;
    }

    public Task<Result<CourseDetailDto, Error>> GetCourseDetailAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetCourseLookupAsync in tests");

    public Task<Result<ModuleDetailDto, Error>> GetModuleDetailAsync(
        Guid moduleId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetModuleLookupAsync in tests");

    public Task<Result<ProjectDetailDto, Error>> GetProjectDetailAsync(
        Guid projectId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetProjectLookupAsync in tests");

    public Task<Result<IssueDetailDto, Error>> GetDetailIssueByIdAsync(
        Guid issueId,
        CancellationToken cancellationToken)
    {
        // Резолвим projectId по issueId из зарегистрированных через AddIssue пар (projectId, issueId).
        // Используется MarkIssueCompleteForUser для построения progress-цепочки студенту без submission'а.
        foreach (IssueLookupKey key in _issues.Keys)
        {
            if (key.IssueId == issueId)
            {
                return Task.FromResult(Result.Success<IssueDetailDto, Error>(new IssueDetailDto(
                    Id: issueId,
                    ProjectId: key.ProjectId,
                    Title: issueId.ToString(),
                    Content: null,
                    Status: "PUBLISHED",
                    AccessType: "ENROLLED",
                    IsAccessible: true,
                    SubmissionMode: "PULL_REQUEST",
                    SelfCheckInstructions: null,
                    RequiresGithubConnection: true,
                    RequiresReviewApp: true,
                    IsAutoReviewEnabled: true,
                    CreatedAt: DateTime.UtcNow,
                    UpdatedAt: DateTime.UtcNow,
                    InternalMaterials: [],
                    ExternalLinks: [])));
            }
        }

        return Task.FromResult(Result.Failure<IssueDetailDto, Error>(
            Error.NotFound("issue.not.found", "Задача не найдена")));
    }

    public Task<Result<CourseSearchLookupDto, Error>> GetCourseSearchLookupAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetCourseLookupAsync in tests");

    public Task<Result<ModuleSearchLookupDto, Error>> GetModuleSearchLookupAsync(
        Guid moduleId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetModuleLookupAsync in tests");

    public Task<Result<ProjectSearchLookupDto, Error>> GetProjectSearchLookupAsync(
        Guid projectId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetProjectLookupAsync in tests");

    public Task<Result<MaterialSearchLookupDto, Error>> GetMaterialSearchLookupAsync(
        Guid materialId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetMaterialLookupAsync in tests");

    public Task<Result<IssueSearchLookupDto, Error>> GetIssueSearchLookupAsync(
        Guid issueId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Use GetIssueLookupAsync in tests");

    public Task<Result<CollectionSearchLookupDto, Error>> GetCollectionSearchLookupAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Collections are not used in ProgressService tests");

    public Task<Result<CourseMaterialIdsDto, Error>> GetCourseMaterialIdsAsync(
        Guid courseId,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Course material ids are not used in ProgressService tests");

    public Task<Result<CourseDto, Error>> GetCourseLookupAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        if (_courses.TryGetValue(courseId, out CourseDto? dto))
        {
            return Task.FromResult(Result.Success<CourseDto, Error>(dto));
        }

        return Task.FromResult(Result.Failure<CourseDto, Error>(
            Error.NotFound("course.not.found", "Курс не найден")));
    }

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAuthorCourseIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = _courses.Values
            .Where(c => c.AuthorId == authorId)
            .Select(c => c.CourseId)
            .OrderBy(id => id)
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<Guid>, Error>(ids));
    }

    public Task<Result<IReadOnlyList<Guid>, Error>> GetAllCourseIdsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = _courses.Values
            .Select(c => c.CourseId)
            .OrderBy(id => id)
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<Guid>, Error>(ids));
    }

    public Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> GetCourseProgressBlueprintsAsync(
        GetCourseProgressBlueprintsRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<CourseProgressBlueprintDto> result = request.CourseIds
            .Distinct()
            .Where(courseId => _courseBlueprints.ContainsKey(courseId))
            .Select(courseId => _courseBlueprints[courseId])
            .ToArray();

        return Task.FromResult(Result.Success<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>(result));
    }

    public Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> ResolveMaterialTargetsAsync(
        ResolveMaterialTargetsRequest request,
        CancellationToken cancellationToken)
    {
        if (_resolveMaterialsUnavailable)
        {
            return Task.FromResult(Result.Failure<IReadOnlyCollection<ResolvedMaterialDto>, Error>(
                Error.Failure("education.content.service.unavailable", "Сервис образовательного контента недоступен")));
        }

        IReadOnlyCollection<ResolvedMaterialDto> result = request.Items
            .Distinct()
            .Where(item => _resolvedMaterials.ContainsKey(new ResolvedMaterialKey(item.CourseId, item.Target.Type, item.Target.Id)))
            .Select(item => _resolvedMaterials[new ResolvedMaterialKey(item.CourseId, item.Target.Type, item.Target.Id)])
            .ToArray();

        return Task.FromResult(Result.Success<IReadOnlyCollection<ResolvedMaterialDto>, Error>(result));
    }

    public Task<Result<ModuleDto, Error>> GetModuleLookupAsync(
        Guid courseId,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        if (_modules.TryGetValue(new ModuleLookupKey(courseId, moduleId), out ModuleDto? dto))
        {
            return Task.FromResult(Result.Success<ModuleDto, Error>(dto));
        }

        return Task.FromResult(Result.Failure<ModuleDto, Error>(
            Error.NotFound("module.not.found", "Модуль не найден")));
    }

    public Task<Result<ProjectDto, Error>> GetProjectLookupAsync(
        Guid courseId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (_projects.TryGetValue(new ProjectLookupKey(courseId, projectId), out ProjectDto? dto))
        {
            return Task.FromResult(Result.Success<ProjectDto, Error>(dto));
        }

        return Task.FromResult(Result.Failure<ProjectDto, Error>(
            Error.NotFound("project.not.found", "Проект не найден")));
    }

    public Task<Result<IssueDto, Error>> GetIssueLookupAsync(
        Guid projectId,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        if (_issues.TryGetValue(new IssueLookupKey(projectId, issueId), out IssueDto? dto))
        {
            return Task.FromResult(Result.Success<IssueDto, Error>(dto));
        }

        return Task.FromResult(Result.Failure<IssueDto, Error>(
            Error.NotFound("issue.not.found", "Задача не найдена")));
    }

    public Task<Result<MaterialDto, Error>> GetMaterialLookupAsync(
        Guid moduleId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        if (_materials.TryGetValue(new MaterialLookupKey(moduleId, materialId), out MaterialDto? dto))
        {
            return Task.FromResult(Result.Success<MaterialDto, Error>(dto));
        }

        return Task.FromResult(Result.Failure<MaterialDto, Error>(
            Error.NotFound("material.not.found", "Материал не найден")));
    }

    public Task<UnitResult<Error>> UpdateMaterialContentAsync(
        Guid materialId,
        UpdateMaterialContentRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(UnitResult.Success<Error>());

    public Task<UnitResult<Error>> UpdateVideoChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(UnitResult.Success<Error>());

    public Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> GetMaterialCourseContextsAsync(
        Guid materialId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MaterialCourseContextDto> result = _materialCourseContexts.TryGetValue(materialId, out List<MaterialCourseContextDto>? contexts)
            ? contexts.ToList()
            : [];

        return Task.FromResult(Result.Success<IReadOnlyList<MaterialCourseContextDto>, Error>(result));
    }

    public Task<Result<EntityOwnershipDto, Error>> GetEntityOwnershipAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (_entityOwnershipUnavailable)
        {
            return Task.FromResult(Result.Failure<EntityOwnershipDto, Error>(
                Error.Failure("education.content.service.unavailable", "Сервис образовательного контента недоступен")));
        }

        if (_entityOwnerships.TryGetValue((entityType, entityId), out EntityOwnershipDto? dto))
        {
            return Task.FromResult(Result.Success<EntityOwnershipDto, Error>(dto));
        }

        return Task.FromResult(Result.Success<EntityOwnershipDto, Error>(
            new EntityOwnershipDto(null, null)));
    }

    public Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> GetMaterialTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<MaterialTitleDto>, Error>(
            Array.Empty<MaterialTitleDto>()));
    }

    public Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> GetMaterialSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<MaterialSummaryDto>, Error>(
            Array.Empty<MaterialSummaryDto>()));
    }

    public Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> GetMaterialCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>(
            Array.Empty<MaterialCourseBindingLookupDto>()));
    }

    public Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> GetIssueCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<IssueCourseBindingLookupDto> result = ids
            .Distinct()
            .Where(id => _issueCourseBindings.ContainsKey(id))
            .Select(id => _issueCourseBindings[id])
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<IssueCourseBindingLookupDto>, Error>(result));
    }

    public Task<Result<IReadOnlyList<CourseTitleDto>, Error>> GetCourseTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<CourseTitleDto> result = ids
            .Distinct()
            .Where(id => _courseTitles.ContainsKey(id))
            .Select(id => new CourseTitleDto(id, _courseTitles[id]))
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<CourseTitleDto>, Error>(result));
    }

    public Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> GetProjectTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<ProjectTitleDto>, Error>(
            Array.Empty<ProjectTitleDto>()));
    }

    public Task<Result<IReadOnlyList<IssueTitleDto>, Error>> GetIssueTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<IssueTitleDto>, Error>(
            Array.Empty<IssueTitleDto>()));
    }

    public Task<Result<QuizAnswerKeyDto, Error>> GetQuizAnswerKeyAsync(
        Guid quizId, CancellationToken cancellationToken)
    {
        if (_quizAnswerKeysUnavailable)
        {
            return Task.FromResult(Result.Failure<QuizAnswerKeyDto, Error>(
                Error.Failure("service.unavailable", "EducationContentService is unavailable.")));
        }

        if (_quizAnswerKeys.TryGetValue(quizId, out QuizAnswerKeyDto? answerKey))
        {
            return Task.FromResult(Result.Success<QuizAnswerKeyDto, Error>(answerKey));
        }

        return Task.FromResult(Result.Failure<QuizAnswerKeyDto, Error>(
            Error.NotFound("quiz.not.found", "Квиз не найден")));
    }

    public Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> GetQuizModuleLookupAsync(
        Guid quizId, CancellationToken cancellationToken)
    {
        IReadOnlyList<QuizModuleContextDto> result =
            _quizModuleContexts.TryGetValue(quizId, out List<QuizModuleContextDto>? contexts)
                ? contexts.ToList()
                : [];

        return Task.FromResult(Result.Success<IReadOnlyList<QuizModuleContextDto>, Error>(result));
    }

    public Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> GetQuizSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<QuizSummaryLookupDto> result = ids
            .Distinct()
            .Where(id => _quizSummaries.ContainsKey(id))
            .Select(id => _quizSummaries[id])
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<QuizSummaryLookupDto>, Error>(result));
    }

    public Task<Result<DigestContentDto, Error>> GetDigestContentAsync(
        DateTime sinceUtc, int maxItemsPerKind, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<DigestContentDto, Error>(new DigestContentDto([], [])));

    private readonly record struct ModuleLookupKey(Guid CourseId, Guid ModuleId);
    private readonly record struct ProjectLookupKey(Guid CourseId, Guid ProjectId);
    private readonly record struct IssueLookupKey(Guid ProjectId, Guid IssueId);
    private readonly record struct MaterialLookupKey(Guid ModuleId, Guid MaterialId);
    private readonly record struct ResolvedMaterialKey(Guid CourseId, EntityType EntityType, Guid EntityId);

    /// <summary>Case-insensitive по entityType, чтобы "issue"/"Issue" матчили один ключ.</summary>
    private sealed class EntityOwnershipKeyComparer : IEqualityComparer<(string EntityType, Guid EntityId)>
    {
        public static readonly EntityOwnershipKeyComparer Instance = new();

        public bool Equals((string EntityType, Guid EntityId) x, (string EntityType, Guid EntityId) y) =>
            string.Equals(x.EntityType, y.EntityType, StringComparison.OrdinalIgnoreCase)
            && x.EntityId == y.EntityId;

        public int GetHashCode((string EntityType, Guid EntityId) obj) =>
            HashCode.Combine(obj.EntityType.ToLowerInvariant(), obj.EntityId);
    }
}
