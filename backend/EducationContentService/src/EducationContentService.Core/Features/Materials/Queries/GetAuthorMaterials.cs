using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Materials;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using ProgressService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetAuthorMaterialsQuery(
    Guid AuthorId,
    string? Cursor,
    int Limit = 20,
    string? Kind = null,
    string? Search = null,
    string? AccessFilter = null) : IQuery;

public sealed class GetAuthorMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("authors/{authorId:guid}/materials", async Task<EndpointResult<CursorResponse<MaterialSummaryDto>>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] string? cursor,
                    [FromQuery] int limit,
                    [FromQuery] string? kind,
                    [FromQuery] string? search,
                    [FromQuery] string? accessFilter,
                    [FromServices] GetAuthorMaterialsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAuthorMaterialsQuery(authorId, cursor, limit == 0 ? 20 : limit, kind, search, accessFilter),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetAuthorMaterialsHandler
    : IQueryHandler<CursorResponse<MaterialSummaryDto>, GetAuthorMaterialsQuery>
{
    private const int MAX_LIMIT = 100;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;

    public GetAuthorMaterialsHandler(
        ITransactionManager transactionManager,
        UserScopedData user,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient)
    {
        _transactionManager = transactionManager;
        _user = user;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
    }

    public async Task<CursorResponse<MaterialSummaryDto>> Handle(
        GetAuthorMaterialsQuery query,
        CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);

        // Anonymous users see only PUBLIC; authenticated users also see REGISTERED.
        // Access types are passed as parameters — no string interpolation in SQL.
        string[] allowedAccessTypes = _user.IsAuthenticated
            ? ["PUBLIC", "REGISTERED"]
            : ["PUBLIC"];

        // accessFilter=free — на этом эндпоинте no-op: дефолтная выдача и так не содержит
        // FREE/ENROLLED (только PUBLIC/REGISTERED). Параметр принимаем чтобы фронт мог
        // безопасно прокидывать `?free=1` единым контрактом во все list-эндпоинты.
        _ = AccessFilterTypes.Resolve(query.AccessFilter);

        string? searchPattern = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : $"%{query.Search.Trim().ToLowerInvariant()}%";

        DbConnection connection = _transactionManager.GetDbConnection();
        var parameters = new
        {
            query.AuthorId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
            Kind = query.Kind,
            AllowedAccessTypes = allowedAccessTypes,
            SearchPattern = searchPattern,
        };

        const string sql = """
                            SELECT
                                m.id,
                                m.author_id,
                                m.title,
                                LEFT(m.content, 280) AS preview,
                                m.kind,
                                m.status,
                                m.access_type,
                                m.created_at,
                                m.updated_at,
                                m.published_at,
                                m.image_id,
                                m.video_id,
                                m.quiz_id,
                                (SELECT COUNT(*)
                                 FROM materials
                                 WHERE author_id = @AuthorId
                                   AND status = 'PUBLISHED'
                                   AND access_type = ANY(@AllowedAccessTypes)
                                   AND (@Kind IS NULL OR kind = @Kind)
                                   AND (@SearchPattern IS NULL OR lower(title) LIKE @SearchPattern)) AS total_count
                            FROM materials m
                            WHERE m.author_id = @AuthorId
                              AND m.status = 'PUBLISHED'
                              AND m.access_type = ANY(@AllowedAccessTypes)
                              AND (@Kind IS NULL OR m.kind = @Kind)
                              AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                              AND (@CursorCreatedAt IS NULL OR (m.created_at, m.id) < (@CursorCreatedAt, @CursorId))
                            ORDER BY m.created_at DESC, m.id DESC
                            LIMIT @Limit;
                            """;

        long totalCount = 0;

        List<MaterialSummaryDto> materials = (await connection.QueryAsync<MaterialSummaryDto, long, MaterialSummaryDto>(
            sql: sql,
            map: (material, count) =>
            {
                totalCount = count;
                return material;
            },
            param: parameters,
            splitOn: "total_count")).ToList();

        bool hasMore = materials.Count > limit;
        if (hasMore)
            materials.RemoveAt(materials.Count - 1);

        string? nextCursor = hasMore
            ? Cursor.Encode(materials[^1].CreatedAt, materials[^1].Id)
            : null;

        materials = await MaterialFeedEnricher.EnrichSummaryThumbnailsAsync(
            materials, _fileServiceClient, _progressServiceClient, cancellationToken);

        return new CursorResponse<MaterialSummaryDto>
        {
            Items = materials,
            NextCursor = nextCursor,
            TotalCount = totalCount
        };
    }
}
