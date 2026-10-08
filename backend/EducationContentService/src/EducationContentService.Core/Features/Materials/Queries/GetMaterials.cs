using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain.Materials;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using ProgressService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetMaterialsQuery(
    string? Scope,
    string? Cursor,
    int Limit = 20,
    string? Kind = null,
    Guid? AuthorId = null,
    string? Search = null) : IQuery;

public sealed class GetMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("materials", async Task<EndpointResult<CursorResponse<MaterialSummaryDto>>> (
                    [AsParameters] GetMaterialsQuery query,
                    [FromServices] GetMaterialsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(query, cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetMaterialsHandler : IQueryHandler<CursorResponse<MaterialSummaryDto>, GetMaterialsQuery>
{
    private const int MAX_LIMIT = 100;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;

    public GetMaterialsHandler(
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient)
    {
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
    }

    public async Task<CursorResponse<MaterialSummaryDto>> Handle(
        GetMaterialsQuery query,
        CancellationToken cancellationToken = default)
    {
        MaterialListScope scope = ParseScope(query.Scope);
        MaterialKind? kindFilter = ParseKind(query.Kind);
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);

        string? searchPattern = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : $"%{query.Search.Trim().ToLowerInvariant()}%";

        DbConnection connection = _transactionManager.GetDbConnection();
        var parameters = new
        {
            AuthorId = scope == MaterialListScope.Public ? query.AuthorId : _userScopedData.UserId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
            KindFilter = kindFilter?.ToString(),
            SearchPattern = searchPattern,
        };

        long totalCount = 0;

        List<MaterialSummaryDto> materials = (await connection.QueryAsync<MaterialSummaryDto, long, MaterialSummaryDto>(
            sql: GetSql(scope),
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

        // Для scope=mine догружаем привязки к курсам — picker модуля/коллекции
        // показывает автору, откуда переиспользуется материал. scope=public не светим
        // топологию чужих материалов; scope=private (только DRAFT-фид) — нет UI-кейса
        // в котором этот massив нужен, лишний round-trip без причины.
        if (scope == MaterialListScope.Mine && materials.Count > 0)
        {
            materials = await EnrichWithCourseBindingsAsync(
                materials, connection, cancellationToken);
        }

        return new CursorResponse<MaterialSummaryDto>
        {
            Items = materials,
            NextCursor = nextCursor,
            TotalCount = totalCount
        };
    }

    private static async Task<List<MaterialSummaryDto>> EnrichWithCourseBindingsAsync(
        List<MaterialSummaryDto> materials,
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        Guid[] materialIds = materials.Select(m => m.Id).ToArray();

        const string sql = """
                           SELECT
                               cm.material_id AS material_id,
                               c.id           AS course_id,
                               c.title        AS title,
                               c.slug         AS slug,
                               c.author_id    AS author_id
                           FROM course_materials cm
                           JOIN courses c ON c.id = cm.course_id
                           WHERE cm.material_id = ANY(@MaterialIds)
                           ORDER BY c.title;
                           """;

        IEnumerable<(Guid MaterialId, MaterialCourseBindingDto Binding)> rows =
            await connection.QueryAsync<Guid, MaterialCourseBindingDto, (Guid, MaterialCourseBindingDto)>(
                new CommandDefinition(
                    sql,
                    new { MaterialIds = materialIds },
                    cancellationToken: cancellationToken),
                map: (id, binding) => (id, binding),
                splitOn: "course_id");

        Dictionary<Guid, List<MaterialCourseBindingDto>> bindingsByMaterial = rows
            .GroupBy(r => r.MaterialId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Binding).ToList());

        return materials
            .Select(m => bindingsByMaterial.TryGetValue(m.Id, out List<MaterialCourseBindingDto>? bindings)
                ? m with { Courses = bindings }
                : m)
            .ToList();
    }

    private static MaterialListScope ParseScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope))
            return MaterialListScope.Public;

        if (string.Equals(scope, "mine", StringComparison.OrdinalIgnoreCase))
            return MaterialListScope.Mine;

        if (string.Equals(scope, "private", StringComparison.OrdinalIgnoreCase))
            return MaterialListScope.Private;

        return MaterialListScope.Public;
    }

    private static MaterialKind? ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        return Enum.TryParse<MaterialKind>(kind, ignoreCase: true, out MaterialKind parsed)
            ? parsed
            : null;
    }

    private static string GetSql(MaterialListScope scope) => scope switch
    {
        MaterialListScope.Private => """
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
                                            AND status = 'DRAFT'
                                            AND (@KindFilter IS NULL OR kind = @KindFilter)
                                            AND (@SearchPattern IS NULL OR lower(title) LIKE @SearchPattern)) AS total_count
                                     FROM materials m
                                     WHERE m.author_id = @AuthorId
                                       AND m.status = 'DRAFT'
                                       AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                       AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                                       AND (@CursorCreatedAt IS NULL OR (m.created_at, m.id) < (@CursorCreatedAt, @CursorId))
                                     ORDER BY m.created_at DESC, m.id DESC
                                     LIMIT @Limit;
                                     """,

        MaterialListScope.Mine => """
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
                                         AND (@KindFilter IS NULL OR kind = @KindFilter)
                                         AND (@SearchPattern IS NULL OR lower(title) LIKE @SearchPattern)) AS total_count
                                  FROM materials m
                                  WHERE m.author_id = @AuthorId
                                    AND (@KindFilter IS NULL OR m.kind = @KindFilter)
                                    AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
                                    AND (@CursorCreatedAt IS NULL OR (m.created_at, m.id) < (@CursorCreatedAt, @CursorId))
                                  ORDER BY m.created_at DESC, m.id DESC
                                  LIMIT @Limit;
                                  """,

        _ => """
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
                  WHERE access_type = 'PUBLIC'
                    AND status = 'PUBLISHED'
                    AND (@AuthorId IS NULL OR author_id = @AuthorId)
                    AND (@KindFilter IS NULL OR kind = @KindFilter)
                    AND (@SearchPattern IS NULL OR lower(title) LIKE @SearchPattern)) AS total_count
             FROM materials m
             WHERE m.access_type = 'PUBLIC'
               AND m.status = 'PUBLISHED'
               AND (@AuthorId IS NULL OR m.author_id = @AuthorId)
               AND (@KindFilter IS NULL OR m.kind = @KindFilter)
               AND (@SearchPattern IS NULL OR lower(m.title) LIKE @SearchPattern)
               AND (@CursorCreatedAt IS NULL OR (m.created_at, m.id) < (@CursorCreatedAt, @CursorId))
             ORDER BY m.created_at DESC, m.id DESC
             LIMIT @Limit;
             """,
    };

    private enum MaterialListScope
    {
        Public,
        Private,
        Mine
    }
}
