using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record GetCourseBuilderQuery(Guid CourseId) : IQuery;

public sealed class GetCourseBuilderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/builder", async Task<EndpointResult<CourseBuilderDto>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseBuilderHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseBuilderQuery(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class GetCourseBuilderHandler : IQueryHandlerWithResult<CourseBuilderDto, GetCourseBuilderQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ILogger<GetCourseBuilderHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public GetCourseBuilderHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        ILogger<GetCourseBuilderHandler> logger,
        UserScopedData userScopedData)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<CourseBuilderDto, Error>> Handle(
        GetCourseBuilderQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                      SELECT
                          c.id, c.author_id, c.slug, c.title, c.description, c.status, c.kind,
                          c.learning_outcomes, c.target_audience, c.prerequisites,
                          c.image_id, c.video_id,
                          c.getting_started_module_id,
                          c.created_at, c.updated_at,
                          c.is_new
                      FROM courses c
                      WHERE c.id = @CourseId;

                      SELECT
                          ci.id, ci.reference_id, ci.item_type, ci.sort_key, ci.is_optional,
                          COALESCE(m.title, p.title) AS title,
                          COALESCE(m.description, p.description) AS description,
                          COALESCE(m.detailed_description, p.detailed_description) AS detailed_description,
                          COALESCE(m.status, p.status) AS status,
                          COALESCE(prc.requires_github_connection, TRUE) AS requires_github_connection,
                          COALESCE(prc.requires_review_app, TRUE) AS requires_review_app,
                          COALESCE(prc.is_auto_review_enabled, TRUE) AS is_auto_review_enabled
                      FROM course_items ci
                      LEFT JOIN modules m ON ci.item_type = 'Module' AND ci.reference_id = m.id
                      LEFT JOIN projects p ON ci.item_type = 'Project' AND ci.reference_id = p.id
                      LEFT JOIN project_review_contexts prc ON ci.item_type = 'Project' AND prc.project_id = p.id
                      WHERE ci.course_id = @CourseId
                      ORDER BY ci.sort_key;

                      SELECT
                          mi.id, mi.module_id, mi.reference_id, mi.item_type, mi.sort_key,
                          mi.is_optional, mi.view_priority,
                          COALESCE(mat.title, i.title, q.title) AS title,
                          COALESCE(mat.status, i.status, q.status) AS status,
                          COALESCE(mat.access_type, i.access_type, q.access_type) AS access_type,
                          mat.video_id AS material_video_id,
                          mat.image_id AS material_image_id,
                          CASE WHEN mi.item_type = 'Quiz' THEN jsonb_array_length(q.questions) END AS questions_count
                      FROM module_items mi
                      JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                      LEFT JOIN materials mat ON mi.item_type = 'Material' AND mat.id = mi.reference_id
                      LEFT JOIN issues i ON mi.item_type = 'Issue' AND i.id = mi.reference_id
                      LEFT JOIN quizzes q ON mi.item_type = 'Quiz' AND q.id = mi.reference_id
                      WHERE ci.course_id = @CourseId
                      ORDER BY mi.module_id, mi.sort_key;
                      """;

        var command = new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(command);

        CourseRow? courseRow = await multi.ReadFirstOrDefaultAsync<CourseRow>();
        if (courseRow is null)
            return GeneralErrors.NotFound(query.CourseId);

        // Author-scoped builder view — only the course owner (or admin) sees the editor DTO.
        // Public consumers use GetCourseLanding / GetCurriculum instead.
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseRow.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var sectionRows = (await multi.ReadAsync<SectionRow>()).ToList();
        var moduleItemRows = (await multi.ReadAsync<ModuleItemRow>()).ToList();

        var moduleItemsByModule = moduleItemRows
            .GroupBy(r => r.ModuleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Batch-fetch covers (manual ImageId + Kinescope video thumbnail fallback).
        // Same priority as MaterialFeedEnricher / GetCurriculum: explicit author intent wins.
        (Dictionary<Guid, string> imageUrlMap, Dictionary<Guid, GetPublicVideoResponse> videoMap) =
            await LoadMediaAsync(moduleItemRows, query.CourseId, cancellationToken);

        List<BuilderSectionDto> sections = [];
        foreach (SectionRow section in sectionRows)
        {
            if (section.Title is null)
            {
                _logger.LogWarning(
                    "Course {CourseId} has course_item {ItemId} ({ItemType}) pointing to a non-existent entity {ReferenceId}",
                    query.CourseId, section.Id, section.ItemType, section.ReferenceId);
                continue;
            }

            List<ModuleItemDto> items = section.ItemType == CourseItemType.Module
                ? moduleItemsByModule
                    .GetValueOrDefault(section.ReferenceId, [])
                    .Select(mi =>
                    {
                        return new ModuleItemDto(
                            mi.Id, mi.ReferenceId, mi.ItemType.ToString(), mi.SortKey, mi.IsOptional,
                            mi.ViewPriority, mi.Title, mi.Status?.ToString(), mi.AccessType,
                            CoverUrl: ResolveCoverUrl(mi, imageUrlMap, videoMap),
                            QuestionsCount: mi.QuestionsCount,
                            QuizId: mi.ItemType == ModuleItemType.Quiz ? mi.ReferenceId : null);
                    })
                    .ToList()
                : [];

            sections.Add(new BuilderSectionDto(
                section.ReferenceId,
                section.ItemType.ToString(),
                section.Title,
                section.Description,
                section.DetailedDescription,
                section.Status?.ToString() ?? "DRAFT",
                section.SortKey,
                section.IsOptional,
                section.RequiresGithubConnection,
                section.RequiresReviewApp,
                section.IsAutoReviewEnabled,
                items));
        }

        return new CourseBuilderDto(
            courseRow.Id, courseRow.AuthorId, courseRow.Slug, courseRow.Title, courseRow.Description,
            courseRow.Status,
            courseRow.Kind,
            courseRow.ImageId, courseRow.VideoId,
            courseRow.GettingStartedModuleId,
            courseRow.CreatedAt, courseRow.UpdatedAt,
            courseRow.IsNew,
            sections,
            courseRow.LearningOutcomes,
            courseRow.TargetAudience,
            courseRow.Prerequisites);
    }

    private sealed class CourseRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string[] LearningOutcomes { get; init; } = [];
        public string[] TargetAudience { get; init; } = [];
        public string[] Prerequisites { get; init; } = [];
        public string Status { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? VideoId { get; init; }
        public Guid? GettingStartedModuleId { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public bool IsNew { get; init; }
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
        public string? DetailedDescription { get; init; }
        public string? Status { get; init; }
        public bool RequiresGithubConnection { get; init; } = true;
        public bool RequiresReviewApp { get; init; } = true;
        public bool IsAutoReviewEnabled { get; init; } = true;
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
        public string? Title { get; init; }
        public string? Status { get; init; }
        public string? AccessType { get; init; }

        public Guid? MaterialVideoId { get; init; }
        public Guid? MaterialImageId { get; init; }
        public int? QuestionsCount { get; init; }
    }

    private async Task<(Dictionary<Guid, string> ImageUrls, Dictionary<Guid, GetPublicVideoResponse> Videos)>
        LoadMediaAsync(
            List<ModuleItemRow> moduleItemRows,
            Guid courseId,
            CancellationToken cancellationToken)
    {
        List<Guid> videoIds = moduleItemRows
            .Where(r => r.MaterialVideoId is not null)
            .Select(r => r.MaterialVideoId!.Value)
            .Distinct()
            .ToList();

        List<Guid> imageIds = moduleItemRows
            .Where(r => r.MaterialImageId is not null)
            .Select(r => r.MaterialImageId!.Value)
            .Distinct()
            .ToList();

        Task<Dictionary<Guid, GetPublicVideoResponse>> videoTask = videoIds.Count > 0
            ? LoadVideosAsync(videoIds, courseId, cancellationToken)
            : Task.FromResult(new Dictionary<Guid, GetPublicVideoResponse>());

        Task<Dictionary<Guid, string>> imageTask = imageIds.Count > 0
            ? LoadImageUrlsAsync(imageIds, courseId, cancellationToken)
            : Task.FromResult(new Dictionary<Guid, string>());

        await Task.WhenAll(videoTask, imageTask);
        return (await imageTask, await videoTask);
    }

    private async Task<Dictionary<Guid, GetPublicVideoResponse>> LoadVideosAsync(
        IReadOnlyList<Guid> videoIds, Guid courseId, CancellationToken ct)
    {
        Dictionary<Guid, GetPublicVideoResponse> map = [];
        const int batchSize = 50;
        foreach (Guid[] chunk in videoIds.Chunk(batchSize))
        {
            Result<List<GetPublicVideoResponse>?, Error> result =
                await _fileServiceClient.GetVideosBatchAsync(chunk, ct);
            if (result is { IsSuccess: true, Value: not null })
            {
                foreach (GetPublicVideoResponse video in result.Value)
                    map[video.Id] = video;
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch video thumbnails for course-builder {CourseId} (chunk of {Count})",
                    courseId, chunk.Length);
            }
        }
        return map;
    }

    private async Task<Dictionary<Guid, string>> LoadImageUrlsAsync(
        IReadOnlyList<Guid> imageIds, Guid courseId, CancellationToken ct)
    {
        Dictionary<Guid, string> map = [];
        const int batchSize = 50;
        foreach (Guid[] chunk in imageIds.Chunk(batchSize))
        {
            Result<List<GetFileResponse>?, Error> result =
                await _fileServiceClient.GetFilesBatchAsync(chunk, ct);
            if (result is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in result.Value)
                {
                    if (file.ContentUrl is not null)
                        map[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch image covers for course-builder {CourseId} (chunk of {Count})",
                    courseId, chunk.Length);
            }
        }
        return map;
    }

    private static string? ResolveCoverUrl(
        ModuleItemRow row,
        IReadOnlyDictionary<Guid, string> imageUrlMap,
        IReadOnlyDictionary<Guid, GetPublicVideoResponse> videoMap)
    {
        // Manual cover wins; Kinescope thumbnail is fallback for VIDEO materials.
        if (row.MaterialImageId is not null
            && imageUrlMap.TryGetValue(row.MaterialImageId.Value, out string? imageUrl))
            return imageUrl;

        if (row.MaterialVideoId is not null
            && videoMap.TryGetValue(row.MaterialVideoId.Value, out GetPublicVideoResponse? video)
            && video.ThumbnailUrl is not null)
            return video.ThumbnailUrl;

        return null;
    }

}
