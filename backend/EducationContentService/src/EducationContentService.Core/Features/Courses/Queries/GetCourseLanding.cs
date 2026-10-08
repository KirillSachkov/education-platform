using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using static Dapper.SqlMapper;
using FileService.Contracts.Assets;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record GetCourseLandingQuery(Guid CourseId) : IQuery;

public sealed class GetCourseLandingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/landing", async Task<EndpointResult<CourseLandingDto>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseLandingHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseLandingQuery(courseId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCourseLandingHandler : IQueryHandlerWithResult<CourseLandingDto, GetCourseLandingQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(3),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly HybridCache _cache;
    private readonly ILogger<GetCourseLandingHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public GetCourseLandingHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IEntitlementChecker entitlementChecker,
        HybridCache cache,
        ILogger<GetCourseLandingHandler> logger,
        UserScopedData userScopedData)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _entitlementChecker = entitlementChecker;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<CourseLandingDto, Error>> Handle(
        GetCourseLandingQuery query, CancellationToken cancellationToken)
    {
        // Determine the cache bucket once per request to avoid per-user cache explosion.
        // Variance is binary (private articles visible or not), so three shared buckets suffice:
        //   anon     — unauthenticated OR authenticated-without-entitlement (public articles only)
        //   manage   — users with Courses.MANAGE or Articles.MANAGE (bypass entitlement check)
        //   enrolled — authenticated users entitled to this course (without manage perm)
        bool canSeePrivateArticles = await CanSeePrivateCourseArticles(query.CourseId, cancellationToken);
        string cacheKey = GetCacheKey(query.CourseId, canSeePrivateArticles);
        Error? fetchError = null;

        CourseLandingDto? cached = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                Result<CourseLandingDto, Error> result = await FetchCourseLanding(query, canSeePrivateArticles, ct);
                if (result.IsFailure)
                {
                    fetchError = result.Error;
                    return null;
                }

                return result.Value;
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (fetchError is not null)
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return fetchError;
        }

        if (cached is null)
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return GeneralErrors.NotFound(query.CourseId);
        }

        return cached;
    }

    private async Task<Result<CourseLandingDto, Error>> FetchCourseLanding(
        GetCourseLandingQuery query, bool canSeePrivateArticles, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                      SELECT
                          c.id, c.author_id, c.slug, c.title, c.description,
                          c.status, c.kind,
                          c.image_id, c.video_id,
                          c.is_new,
                          c.created_at, c.updated_at
                      FROM courses c
                      WHERE c.id = @CourseId;

                      SELECT
                          ci.id, ci.reference_id, ci.item_type, ci.sort_key, ci.is_optional,
                          COALESCE(m.title, p.title) AS title,
                          COALESCE(m.description, p.description) AS description,
                          COALESCE(m.status, p.status) AS status
                      FROM course_items ci
                      LEFT JOIN modules m ON ci.item_type = 'Module' AND ci.reference_id = m.id
                      LEFT JOIN projects p ON ci.item_type = 'Project' AND ci.reference_id = p.id
                      WHERE ci.course_id = @CourseId
                        AND COALESCE(m.status, p.status) = 'PUBLISHED'
                      ORDER BY ci.sort_key;

                      SELECT
                          mi.id, mi.module_id, mi.reference_id, mi.item_type, mi.sort_key, mi.is_optional,
                          mi.view_priority,
                          COALESCE(mat.title, i.title) AS title,
                          COALESCE(mat.status, i.status) AS status,
                          COALESCE(mat.access_type, i.access_type) AS access_type
                      FROM module_items mi
                      JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                      LEFT JOIN materials mat ON mi.item_type = 'Material' AND mat.id = mi.reference_id
                      LEFT JOIN issues i ON mi.item_type = 'Issue' AND i.id = mi.reference_id
                      WHERE ci.course_id = @CourseId
                        AND COALESCE(mat.status, i.status) = 'PUBLISHED'
                      ORDER BY mi.module_id, mi.sort_key;

                      SELECT
                          pi.id, pi.project_id, pi.issue_id, pi.sort_key, pi.is_optional,
                          i.title, i.status, i.access_type
                      FROM project_items pi
                      JOIN course_items ci ON ci.reference_id = pi.project_id AND ci.item_type = 'Project'
                      JOIN issues i ON i.id = pi.issue_id
                      WHERE ci.course_id = @CourseId
                        AND i.status = 'PUBLISHED'
                      ORDER BY pi.project_id, pi.sort_key;

                      -- Тесты курса (#551): DISTINCT PUBLISHED-квизы из module_items модулей курса —
                      -- зеркало quiz_refs в GetCourseProgressBlueprints (один квиз может быть размещён
                      -- в нескольких модулях, считается один раз).
                      SELECT COUNT(DISTINCT q.id)::integer
                      FROM module_items mi
                      JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                      JOIN quizzes q ON q.id = mi.reference_id AND q.status = 'PUBLISHED'
                      WHERE ci.course_id = @CourseId
                        AND mi.item_type = 'Quiz';
                      """;

        var command = new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken);
        await using GridReader multi = await connection.QueryMultipleAsync(command);

        CourseRow? courseRow = await multi.ReadFirstOrDefaultAsync<CourseRow>();
        if (courseRow is null)
            return GeneralErrors.NotFound(query.CourseId);

        if (courseRow.Status != PublicationStatus.PUBLISHED)
            return GeneralErrors.NotFound(query.CourseId);

        var sectionRows = (await multi.ReadAsync<SectionRow>()).ToList();
        var moduleItemRows = (await multi.ReadAsync<ModuleItemRow>()).ToList();
        var projectItemRows = (await multi.ReadAsync<ProjectItemRow>()).ToList();
        int quizCount = await multi.ReadFirstAsync<int>();

        // После унификации Lesson+Article → Material is_public больше не применяется —
        // доступ определяется AccessType на материале.

        var moduleItemsByModule = moduleItemRows.GroupBy(r => r.ModuleId).ToDictionary(g => g.Key, g => g.ToList());
        var projectItemsByProject = projectItemRows.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        int moduleCount = 0;
        int lessonCount = 0;
        int issueCount = 0;

        List<CurriculumSectionDto> sections = [];
        int globalModulePosition = 1;
        foreach (SectionRow section in sectionRows)
        {
            if (section.Title is null)
            {
                _logger.LogWarning(
                    "Course {CourseId} has course_item {ItemId} ({ItemType}) pointing to a non-existent entity {ReferenceId}",
                    query.CourseId, section.Id, section.ItemType, section.ReferenceId);
                continue;
            }

            List<CurriculumItemDto>? items = section.ItemType switch
            {
                CourseItemType.Module => moduleItemsByModule
                    .GetValueOrDefault(section.ReferenceId, [])
                    .Select(mi => new CurriculumItemDto(
                        mi.ReferenceId, mi.ItemType.ToString(), mi.Title, mi.SortKey, mi.IsOptional,
                        globalModulePosition++, mi.AccessType, mi.ViewPriority))
                    .ToList(),

                CourseItemType.Project => projectItemsByProject
                    .GetValueOrDefault(section.ReferenceId, [])
                    .Select((pi, idx) => new CurriculumItemDto(
                        pi.IssueId, nameof(ModuleItemType.Issue), pi.Title, pi.SortKey, pi.IsOptional,
                        idx + 1, pi.AccessType))
                    .ToList(),

                _ => null
            };

            if (items is null)
            {
                _logger.LogWarning(
                    "Course {CourseId} has course_item {ItemId} with unknown CourseItemType {ItemType}; skipping",
                    query.CourseId, section.Id, section.ItemType);
                continue;
            }

            switch (section.ItemType)
            {
                case CourseItemType.Module:
                    moduleCount++;
                    lessonCount += items.Count(i => string.Equals(i.ItemType, nameof(ModuleItemType.Material), StringComparison.Ordinal));
                    issueCount += items.Count(i => string.Equals(i.ItemType, nameof(ModuleItemType.Issue), StringComparison.Ordinal));
                    break;
                case CourseItemType.Project:
                    issueCount += items.Count;
                    break;
            }

            sections.Add(new CurriculumSectionDto(
                section.ReferenceId, section.ItemType.ToString(), section.Title, section.Description,
                null, section.SortKey, section.IsOptional, items));
        }

        string? imageUrl = null;
        if (courseRow.ImageId is not null)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(courseRow.ImageId.Value, cancellationToken);

            if (fileResult is { IsSuccess: true, Value: not null })
                imageUrl = fileResult.Value.ContentUrl;
            else
                _logger.LogWarning(
                    "Failed to fetch image {ImageId} for course {CourseId}",
                    courseRow.ImageId,
                    query.CourseId);
        }

        var stats = new CourseLandingStatsDto(moduleCount, lessonCount, issueCount, quizCount);

        // Курс «имеет бесплатно-доступный контент», если хотя бы один опубликованный урок или
        // задание помечен как PUBLIC/REGISTERED — это разрешает кнопку «Попробовать бесплатно»
        // на платном курсе. Issue #358: AccessType.FREE удалён → его роль играет REGISTERED.
        bool hasFreeContent =
            moduleItemRows.Any(r =>
                string.Equals(r.AccessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.AccessType, "REGISTERED", StringComparison.OrdinalIgnoreCase))
            || projectItemRows.Any(r =>
                string.Equals(r.AccessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.AccessType, "REGISTERED", StringComparison.OrdinalIgnoreCase));

        return new CourseLandingDto(
            courseRow.Id, courseRow.AuthorId, courseRow.Slug, courseRow.Title, courseRow.Description,
            courseRow.Status.ToString(),
            courseRow.Kind,
            courseRow.ImageId, imageUrl, courseRow.VideoId,
            stats,
            hasFreeContent,
            courseRow.IsNew,
            courseRow.CreatedAt, courseRow.UpdatedAt,
            sections);
    }

    private sealed class CourseRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public PublicationStatus Status { get; init; }
        public string Kind { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? VideoId { get; init; }
        public bool IsNew { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class SectionRow
    {
        public Guid Id { get; init; }
        public Guid ReferenceId { get; init; }
        public CourseItemType ItemType { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public PublicationStatus? Status { get; init; }
    }

    private sealed class ModuleItemRow
    {
        public Guid Id { get; init; }
        public Guid ModuleId { get; init; }
        public Guid ReferenceId { get; init; }
        public ModuleItemType ItemType { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string ViewPriority { get; init; } = null!;
        public string Title { get; init; } = null!;
        public PublicationStatus Status { get; init; }
        public string? AccessType { get; init; }
    }

    private sealed class ProjectItemRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public Guid IssueId { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string Title { get; init; } = null!;
        public PublicationStatus Status { get; init; }
        public string? AccessType { get; init; }
    }

    private async Task<bool> CanSeePrivateCourseArticles(Guid courseId, CancellationToken cancellationToken)
    {
        if (_userScopedData.IsAuthenticated && (
                _userScopedData.HasPermission(PlatformPermissions.Courses.MANAGE) ||
                _userScopedData.HasPermission(PlatformPermissions.Articles.MANAGE)))
            return true;

        if (!_userScopedData.IsAuthenticated)
            return false;

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _userScopedData.ToAccessSubject(),
            ResourceTypes.COURSE,
            courseId,
            cancellationToken);

        return decision.IsGranted;
    }

    private string GetCacheKey(Guid courseId, bool canSeePrivateArticles)
    {
        if (_userScopedData.IsAuthenticated && (
                _userScopedData.HasPermission(PlatformPermissions.Courses.MANAGE) ||
                _userScopedData.HasPermission(PlatformPermissions.Articles.MANAGE)))
            return $"course-landing:{courseId}:manage";

        return canSeePrivateArticles
            ? $"course-landing:{courseId}:enrolled"
            : $"course-landing:{courseId}:anon";
    }
}
