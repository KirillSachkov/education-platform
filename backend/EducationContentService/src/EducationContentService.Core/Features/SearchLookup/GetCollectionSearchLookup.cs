using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.SearchLookup;
using EducationContentService.Core.Features.ContentAccess;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.SearchLookup;

public sealed record GetCollectionSearchLookupQuery(Guid CollectionId) : IQuery;

public sealed class GetCollectionSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/collections/{collectionId:guid}", async Task<EndpointResult<CollectionSearchLookupDto>> (
                [FromRoute] Guid collectionId,
                [FromServices] GetCollectionSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetCollectionSearchLookupQuery(collectionId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetCollectionSearchLookupHandler
    : IQueryHandlerWithResult<CollectionSearchLookupDto, GetCollectionSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCollectionSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CollectionSearchLookupDto, Error>> Handle(
        GetCollectionSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                coll.id,
                coll.author_id,
                coll.title,
                coll.description,
                coll.cover_image_id AS image_id,
                coll.course_id,
                coll.status,
                coll.access_type,
                coll.updated_at,
                c.slug AS course_slug,
                c.title AS course_title
            FROM collections coll
            LEFT JOIN courses c
                ON c.id = coll.course_id
            WHERE coll.id = @CollectionId;
            """;

        CollectionSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<CollectionSearchLookupRow>(
            new CommandDefinition(sql, new { query.CollectionId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.CollectionId);
        }

        return new CollectionSearchLookupDto(
            row.Id,
            row.CourseId,
            row.CourseSlug,
            row.Title,
            row.Description,
            row.ImageId,
            row.CourseTitle,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            ContentAccessTagBuilder.Build(
                row.AccessType,
                row.Id,
                row.CourseId.HasValue ? [row.CourseId.Value] : []),
            row.UpdatedAt,
            row.AuthorId);
    }

    private sealed class CollectionSearchLookupRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseSlug { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public Guid? ImageId { get; init; }
        public string? CourseTitle { get; init; }
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
    }
}
