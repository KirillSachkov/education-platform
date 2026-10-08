using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Domain;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using ProgressService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetMaterialDetailQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("materials/{materialId:guid}/detail", async Task<EndpointResult<MaterialDetailDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetMaterialDetailHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialDetailQuery(materialId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetMaterialDetailHandler : IQueryHandlerWithResult<MaterialDetailDto, GetMaterialDetailQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;
    private readonly ILogger<GetMaterialDetailHandler> _logger;

    public GetMaterialDetailHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient,
        IAuthorLookupClient authorLookupClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData,
        ILogger<GetMaterialDetailHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
        _authorLookupClient = authorLookupClient;
        _entitlementChecker = entitlementChecker;
        _userData = userData;
        _logger = logger;
    }

    public async Task<Result<MaterialDetailDto, Error>> Handle(
        GetMaterialDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               m.id,
                               m.author_id,
                               m.title,
                               m.content,
                               m.description,
                               m.kind,
                               m.status,
                               m.access_type,
                               m.image_id,
                               m.video_id,
                               m.quiz_id,
                               (
                                   SELECT COUNT(*)
                                   FROM course_materials cm
                                   WHERE cm.material_id = m.id
                               ) AS course_count,
                               EXISTS (
                                   SELECT 1
                                   FROM course_materials cmo
                                   JOIN courses c ON c.id = cmo.course_id
                                   WHERE cmo.material_id = m.id AND c.author_id = @UserId
                               ) AS is_course_owner,
                               m.created_at,
                               m.updated_at,
                               m.chapter_titles,
                               m.chapter_timestamps
                           FROM materials m
                           WHERE m.id = @MaterialId
                             AND (
                                   m.status = 'PUBLISHED'
                                   OR m.author_id = @UserId
                                   OR @IsContentManager
                                   OR EXISTS (
                                       SELECT 1
                                       FROM course_materials cmo2
                                       JOIN courses c2 ON c2.id = cmo2.course_id
                                       WHERE cmo2.material_id = m.id AND c2.author_id = @UserId
                                   )
                             );
                           """;

        var parameters = new
        {
            query.MaterialId,
            UserId = _userData.IsAuthenticated ? _userData.UserId : Guid.Empty,
            // admin / content-moderator see any material (incl. DRAFT) for management — Tier-2 bypass (#657).
            // Course owner is handled separately by the is_course_owner EXISTS in the WHERE clause — do NOT
            // fold it into IsContentManager (it would skip the per-row course check).
            IsContentManager = _userData.IsAdmin
                || _userData.HasPermission(PlatformPermissions.Content.MODERATE)
        };

        MaterialDetailRow? row = await connection.QueryFirstOrDefaultAsync<MaterialDetailRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (row is null)
            return EducationErrors.MaterialNotFound(query.MaterialId);

        // Short-circuit по данным из БД: PUBLIC — всегда доступен, автору — всегда доступен.
        // Экономит 2-3 Redis round-trips на самом горячем read-path (detail страница)
        // и делает PUBLIC-контент устойчивым к падению Redis (fail-closed не скроет открытое).
        bool isAuthor = _userData.IsAuthenticated && row.AuthorId == _userData.UserId;
        bool isPublic = string.Equals(row.AccessType, "PUBLIC", StringComparison.Ordinal);
        bool isPublished = string.Equals(row.Status, "PUBLISHED", StringComparison.Ordinal);

        // Менеджеры, которые управляют контентом (а не потребляют его как студенты), минуют
        // Tier-3 entitlement: автор материала, admin и владелец курса, в котором лежит материал (#657).
        // Content-moderator СОЗНАТЕЛЬНО НЕ здесь — он минует Tier-2 ownership (видит DRAFT через
        // @IsContentManager в SQL WHERE), но Tier-3 entitlement на него распространяется
        // (CLAUDE.md: moderate bypass'ит только Tier-2, не Tier-3 → не получает платный контент даром).
        bool bypassesEntitlement = isAuthor || _userData.IsAdmin || row.IsCourseOwner;

        // Entitlement (Tier-3) применяется только к PUBLISHED gated-контенту (это потребление).
        // DRAFT/ARCHIVED сюда доходит лишь когда caller прошёл SQL WHERE как менеджер — редакторский
        // просмотр, entitlement не нужен (черновик не продаётся, и Redis-тегов у него нет →
        // fail-closed дал бы ложный 403 даже модератору/владельцу курса).
        if (isPublished && !isPublic && !bypassesEntitlement)
        {
            // Проверяем доступ ДО HTTP-вызовов к FileService, чтобы не тратить I/O на неавторизованные запросы.
            AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
                _userData.ToAccessSubject(),
                ResourceTypes.MATERIAL,
                query.MaterialId,
                cancellationToken);

            if (!decision.IsGranted)
            {
                _logger.LogInformation(
                    "Access denied to material {MaterialId} for user {UserId}",
                    query.MaterialId, _userData.UserId);

                return _userData.IsAuthenticated
                    ? EducationErrors.AccessDenied()
                    : EducationErrors.UnauthorizedAccess();
            }
        }

        string? imageUrl = null;
        if (row.ImageId is not null)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(row.ImageId.Value, cancellationToken);

            if (fileResult is { IsSuccess: true, Value: not null })
            {
                imageUrl = fileResult.Value.ContentUrl;
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch image {ImageId} for material {MaterialId}",
                    row.ImageId, query.MaterialId);
            }
        }

        MaterialVideoDto? video = null;
        if (row.VideoId is not null)
        {
            Result<GetVideoResponse?, Error> videoResult =
                await _fileServiceClient.GetVideoAsync(row.VideoId.Value, cancellationToken);

            if (videoResult is { IsSuccess: true, Value: not null })
            {
                GetVideoResponse v = videoResult.Value;
                video = new MaterialVideoDto(v.ExternalVideoId, v.ThumbnailUrl, v.DurationSeconds, v.Status);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch video {VideoId} for material {MaterialId}",
                    row.VideoId, query.MaterialId);
            }
        }

        IReadOnlyList<MaterialChapterDto> chapters = BuildChapters(
            row.ChapterTitles, row.ChapterTimestamps);

        long viewsCount = await GetViewsCountAsync(row.Id, cancellationToken);

        (string? authorName, string? authorAvatarUrl) =
            await ResolveAuthorCreditAsync(row.AuthorId, cancellationToken);

        return new MaterialDetailDto(
            row.Id,
            row.AuthorId,
            row.Title,
            row.Content,
            row.Description,
            row.Kind,
            row.Status,
            row.AccessType,
            IsAccessible: true,
            row.ImageId,
            imageUrl,
            row.VideoId,
            video,
            row.QuizId,
            row.CreatedAt,
            row.UpdatedAt,
            row.CourseCount,
            chapters,
            AuthorDisplayName: authorName,
            AuthorAvatarUrl: authorAvatarUrl)
        {
            ViewsCount = viewsCount,
        };
    }

    // Resolves the author's display name + avatar URL (#569). Both best-effort — a degraded
    // AuthService / FileService leaves the credit null and never fails the material response.
    private async Task<(string? DisplayName, string? AvatarUrl)> ResolveAuthorCreditAsync(
        Guid authorId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync([authorId], cancellationToken);

        if (authorsResult.IsFailure
            || !authorsResult.Value.TryGetValue(authorId, out AuthorCreditDto? author))
        {
            return (null, null);
        }

        string? avatarUrl = null;
        if (author.AvatarId is { } avatarId)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(avatarId, cancellationToken);
            if (fileResult is { IsSuccess: true, Value: not null })
                avatarUrl = fileResult.Value.ContentUrl;
        }

        return (author.DisplayName, avatarUrl);
    }

    private async Task<long> GetViewsCountAsync(Guid materialId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<Guid, long>, Error> countsResult =
            await _progressServiceClient.GetMaterialViewsCountsAsync([materialId], cancellationToken);

        if (countsResult.IsFailure)
        {
            // Graceful degradation: счётчик не блокирует рендер материала.
            _logger.LogWarning(
                "Failed to fetch views count for material {MaterialId}: {Error}",
                materialId, countsResult.Error.Messages[0].Code);
            return 0;
        }

        return countsResult.Value.TryGetValue(materialId, out long c) ? c : 0L;
    }

    private static IReadOnlyList<MaterialChapterDto> BuildChapters(
        string[]? titles, int[]? offsets)
    {
        if (titles is null || offsets is null || titles.Length == 0)
            return [];

        // Defensive min на случай рассинхрона длин в легаси-данных (domain-инвариант гарантирует равенство).
        int count = Math.Min(titles.Length, offsets.Length);
        var result = new List<MaterialChapterDto>(count);
        for (int i = 0; i < count; i++)
            result.Add(new MaterialChapterDto(titles[i], offsets[i]));

        result.Sort(static (a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
        return result;
    }

    private sealed class MaterialDetailRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Content { get; init; }
        public string? Description { get; init; }
        public string Kind { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? VideoId { get; init; }
        public Guid? QuizId { get; init; }
        public int CourseCount { get; init; }
        public bool IsCourseOwner { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string[]? ChapterTitles { get; init; }
        public int[]? ChapterTimestamps { get; init; }
    }
}
