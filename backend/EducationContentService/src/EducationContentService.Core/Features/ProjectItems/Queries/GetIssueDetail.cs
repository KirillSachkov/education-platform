using System.Data.Common;
using System.Text.Json;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Domain;
using EducationContentService.Contracts.Issues;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Core.Features.ProjectItems.UseCases;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using FileService.Contracts.HttpCommunication;

namespace EducationContentService.Core.Features.ProjectItems.Queries;

public sealed record GetIssueDetailQuery(Guid IssueId) : IQuery;

public sealed class GetIssueDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("issues/{issueId:guid}/detail", async Task<EndpointResult<IssueDetailDto>> (
            [FromRoute] Guid issueId,
            [FromServices] GetIssueDetailHandler handler,
            CancellationToken cancellationToken) =>
                await handler.Handle(new GetIssueDetailQuery(issueId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetIssueDetailHandler : IQueryHandlerWithResult<IssueDetailDto, GetIssueDetailQuery>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly UserScopedData _user;

    public GetIssueDetailHandler(
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IFileServiceClient fileServiceClient,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _fileServiceClient = fileServiceClient;
        _user = user;
    }

    public async Task<Result<IssueDetailDto, Error>> Handle(
        GetIssueDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                i.id,
                i.author_id,
                i.project_id,
                i.title,
                i.content,
                i.status,
                i.access_type,
                i.submission_mode,
                i.self_check_instructions,
                COALESCE(prc.requires_github_connection, TRUE) AS requires_github_connection,
                COALESCE(prc.requires_review_app, TRUE) AS requires_review_app,
                (
                    COALESCE(prc.is_auto_review_enabled, TRUE)
                    AND COALESCE(rs.is_auto_review_enabled, TRUE)
                    AND i.submission_mode = 'PULL_REQUEST'
                ) AS is_auto_review_enabled,
                i.created_at,
                i.updated_at,
                i.internal_materials::text AS internal_materials_json,
                i.external_links::text AS external_links_json
            FROM issues i
            LEFT JOIN project_review_contexts prc ON prc.project_id = i.project_id
            LEFT JOIN review_specs rs ON rs.issue_id = i.id
            WHERE i.id = @IssueId;
            """;

        var raw = await connection.QueryFirstOrDefaultAsync<IssueDetailRawDto>(
            sql, new { query.IssueId });

        if (raw is null)
            return GeneralErrors.NotFound(query.IssueId);

        bool canManageIssue = _user.CheckOwnership(raw.AuthorId).IsSuccess;
        bool isPublished = string.Equals(
            raw.Status,
            nameof(PublicationStatus.PUBLISHED),
            StringComparison.OrdinalIgnoreCase);
        if (!isPublished && !canManageIssue)
            return GeneralErrors.NotFound(query.IssueId);

        // Short-circuit по данным из БД: PUBLIC — всегда доступен всем. Экономит
        // 2-3 Redis round-trips на detail-странице и делает PUBLIC-задания устойчивыми
        // к недоступности Redis (fail-closed не скроет открытое).
        bool isPublic = string.Equals(raw.AccessType, "PUBLIC", StringComparison.Ordinal);

        if (!isPublic && !canManageIssue)
        {
            // Проверяем доступ через entitlement checker для всех AccessType,
            // включая анонимов — если появятся PUBLIC-задания вне этой ветки,
            // fail-closed сработает как положено.
            AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
                _user.ToAccessSubject(), ResourceTypes.ISSUE, query.IssueId, cancellationToken);

            if (!decision.IsGranted)
            {
                return _user.IsAuthenticated
                    ? EducationErrors.AccessDenied()
                    : EducationErrors.UnauthorizedAccess();
            }
        }

        var internalMaterials = DeserializeJson<List<InternalMaterialRaw>>(raw.InternalMaterialsJson) ?? [];
        var externalLinks = DeserializeJson<List<ExternalLinkRaw>>(raw.ExternalLinksJson) ?? [];

        // Resolve material titles for internal materials
        var referenceIds = internalMaterials
            .Where(m => m.ReferenceId != Guid.Empty
                        && string.Equals(
                            m.ItemType,
                            "Material",
                            StringComparison.OrdinalIgnoreCase))
            .Select(m => m.ReferenceId)
            .Distinct()
            .Take(UpdateIssueInternalMaterialsRequestValidator.MAX_ITEMS)
            .ToArray();

        Dictionary<Guid, string> titleMap = [];
        Dictionary<Guid, Guid?> imageIdMap = [];
        Dictionary<Guid, Guid?> videoIdMap = [];
        if (referenceIds.Length > 0)
        {
            const string materialSql = """
                SELECT mat.id, mat.author_id, mat.title, mat.status, mat.access_type,
                       mat.image_id, mat.video_id
                FROM materials mat
                WHERE mat.id = ANY(@Ids)
                """;

            List<MaterialRow> materials = (await connection.QueryAsync<MaterialRow>(
                    materialSql,
                    new { Ids = referenceIds }))
                .ToList();
            var visibleMaterials = new List<MaterialRow>(materials.Count);
            var entitlementMaterialIds = new List<Guid>();

            foreach (MaterialRow material in materials)
            {
                bool callerCanManageMaterial = _user.CheckOwnership(material.AuthorId).IsSuccess;
                bool materialIsPublished = string.Equals(
                    material.Status,
                    nameof(PublicationStatus.PUBLISHED),
                    StringComparison.OrdinalIgnoreCase);
                bool materialIsPublic = string.Equals(
                    material.AccessType,
                    nameof(AccessType.PUBLIC),
                    StringComparison.OrdinalIgnoreCase);
                bool referenceAllowedByIssueAuthor = material.AuthorId == raw.AuthorId
                                                     || (materialIsPublished && materialIsPublic);

                if (!callerCanManageMaterial && !referenceAllowedByIssueAuthor)
                    continue;

                if (callerCanManageMaterial || (materialIsPublished && materialIsPublic))
                    visibleMaterials.Add(material);
                else if (materialIsPublished)
                    entitlementMaterialIds.Add(material.Id);
            }

            if (entitlementMaterialIds.Count > 0)
            {
                IReadOnlyDictionary<Guid, AccessDecision> decisions =
                    await _entitlementChecker.CheckAccessBatchAsync(
                        _user.ToAccessSubject(),
                        ResourceTypes.MATERIAL,
                        entitlementMaterialIds,
                        cancellationToken);
                visibleMaterials.AddRange(materials.Where(material =>
                    decisions.TryGetValue(material.Id, out AccessDecision? decision)
                    && decision.IsGranted));
            }

            foreach (MaterialRow material in visibleMaterials.DistinctBy(x => x.Id))
            {
                titleMap[material.Id] = material.Title;
                imageIdMap[material.Id] = material.ImageId;
                videoIdMap[material.Id] = material.VideoId;
            }
        }

        // Resolve thumbnails: prefer video thumbnail, fallback to lesson image
        // 1. Fetch video thumbnails
        Dictionary<Guid, string> thumbnailMap = []; // lessonId → thumbnailUrl
        var videoIds = videoIdMap.Values
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> videoThumbnails = [];
        if (videoIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetVideosBatchAsync(videoIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (var video in batchResult.Value)
                {
                    if (video.ThumbnailUrl is not null)
                        videoThumbnails[video.Id] = video.ThumbnailUrl;
                }
            }
        }

        // 2. Batch-fetch image URLs
        var imageIds = imageIdMap.Values
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> imageUrlMap = [];
        if (imageIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(imageIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (var file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        imageUrlMap[file.Id] = file.ContentUrl;
                }
            }
        }

        // 3. Build per-lesson thumbnail: video thumbnail → image URL
        foreach (Guid lessonId in referenceIds)
        {
            var vidId = videoIdMap.GetValueOrDefault(lessonId);
            if (vidId.HasValue && videoThumbnails.TryGetValue(vidId.Value, out string? vidThumb))
            {
                thumbnailMap[lessonId] = vidThumb;
                continue;
            }

            var imgId = imageIdMap.GetValueOrDefault(lessonId);
            if (imgId.HasValue && imageUrlMap.TryGetValue(imgId.Value, out string? imgUrl))
            {
                thumbnailMap[lessonId] = imgUrl;
            }
        }

        HashSet<Guid> visibleMaterialIds = titleMap.Keys.ToHashSet();
        List<InternalMaterialRaw> visibleInternalMaterials = internalMaterials
            .Where(m => visibleMaterialIds.Contains(m.ReferenceId)
                        && string.Equals(
                            m.ItemType,
                            "Material",
                            StringComparison.OrdinalIgnoreCase))
            .DistinctBy(m => m.ReferenceId)
            .Take(UpdateIssueInternalMaterialsRequestValidator.MAX_ITEMS)
            .ToList();

        var issue = new IssueDetailDto(
            raw.Id,
            raw.ProjectId,
            raw.Title,
            raw.Content,
            raw.Status,
            raw.AccessType,
            IsAccessible: true,
            raw.SubmissionMode,
            raw.SelfCheckInstructions,
            raw.RequiresGithubConnection,
            raw.RequiresReviewApp,
            raw.IsAutoReviewEnabled,
            raw.CreatedAt,
            raw.UpdatedAt,
            visibleInternalMaterials.Select(m => new IssueInternalMaterialDto(
                m.ItemType,
                m.ReferenceId,
                m.IsRequired,
                titleMap.GetValueOrDefault(m.ReferenceId),
                thumbnailMap.GetValueOrDefault(m.ReferenceId))).ToList(),
            externalLinks.Select(e => new IssueExternalLinkDto(
                e.Url,
                e.Title,
                e.IsRequired)).ToList());

        return issue;
    }

    private static T? DeserializeJson<T>(string? json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json, JsonOptions);

    private sealed class IssueDetailRawDto
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public Guid ProjectId { get; init; }
        public string Title { get; init; } = null!;
        public string? Content { get; init; }
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public string SubmissionMode { get; init; } = "PULL_REQUEST";
        public string? SelfCheckInstructions { get; init; }
        public bool RequiresGithubConnection { get; init; } = true;
        public bool RequiresReviewApp { get; init; } = true;
        public bool IsAutoReviewEnabled { get; init; } = true;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string? InternalMaterialsJson { get; init; }
        public string? ExternalLinksJson { get; init; }
    }

    private sealed class InternalMaterialRaw
    {
        public string ItemType { get; init; } = null!;
        public Guid ReferenceId { get; init; }
        public bool IsRequired { get; init; }
    }

    private sealed class ExternalLinkRaw
    {
        public string Url { get; init; } = null!;
        public string Title { get; init; } = null!;
        public bool IsRequired { get; init; }
    }

    private sealed class MaterialRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? VideoId { get; init; }
    }
}
