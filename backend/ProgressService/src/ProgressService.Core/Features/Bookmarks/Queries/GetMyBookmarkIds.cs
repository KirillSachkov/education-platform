using System.Data.Common;
using Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;

namespace ProgressService.Core.Features.Bookmarks.Queries;

public sealed record GetMyBookmarkIdsQuery(
    IReadOnlyList<Guid> CourseIds,
    string? Cursor,
    int Limit) : IQuery;

public sealed record GetMyBookmarkIdsQueryParams(
    string? CourseIds,
    string? Cursor,
    int? Limit);

public sealed class GetMyBookmarkIdsQueryValidator : AbstractValidator<GetMyBookmarkIdsQuery>
{
    public GetMyBookmarkIdsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 500)
            .WithError(GeneralErrors.ValueIsInvalid("limit"));
        RuleFor(x => x.CourseIds.Count)
            .LessThanOrEqualTo(50)
            .WithError(GeneralErrors.ValueIsInvalid("courseIds"));
    }
}

public sealed class GetMyBookmarkIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/bookmarks/me/ids",
                async Task<EndpointResult<CursorResponse<BookmarkIdDto>>> (
                    [AsParameters] GetMyBookmarkIdsQueryParams queryParams,
                    [FromServices] GetMyBookmarkIdsHandler handler,
                    CancellationToken cancellationToken) =>
                {
                    IReadOnlyList<Guid> courseIds = ParseCourseIds(queryParams.CourseIds);
                    return await handler.Handle(
                        new GetMyBookmarkIdsQuery(
                            courseIds,
                            queryParams.Cursor,
                            queryParams.Limit ?? 200),
                        cancellationToken);
                })
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }

    private static IReadOnlyList<Guid> ParseCourseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<Guid> ids = new(parts.Length);
        foreach (string part in parts)
        {
            if (Guid.TryParse(part, out Guid id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

public sealed class GetMyBookmarkIdsHandler
    : IQueryHandlerWithResult<CursorResponse<BookmarkIdDto>, GetMyBookmarkIdsQuery>
{
    private readonly IValidator<GetMyBookmarkIdsQuery> _validator;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetMyBookmarkIdsHandler(
        IValidator<GetMyBookmarkIdsQuery> validator,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _validator = validator;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<Result<CursorResponse<BookmarkIdDto>, Error>> Handle(
        GetMyBookmarkIdsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Cursor? cursor = Cursor.Decode(query.Cursor);
        DbConnection connection = _transactionManager.GetDbConnection();
        Guid[]? courseIdsFilter = query.CourseIds.Count > 0 ? query.CourseIds.ToArray() : null;

        string sql = cursor is null
            ? """
              SELECT
                  mb.id AS Id,
                  mb.course_id AS CourseId,
                  mb.target_entity_type AS TargetType,
                  mb.target_entity_id AS TargetId,
                  mb.created_at AS CreatedAt,
                  (
                      SELECT COUNT(*)
                      FROM material_bookmarks
                      WHERE user_id = @UserId
                        AND (@CourseIds::uuid[] IS NULL OR course_id = ANY(@CourseIds))
                  ) AS TotalCount
              FROM material_bookmarks mb
              WHERE mb.user_id = @UserId
                AND (@CourseIds::uuid[] IS NULL OR mb.course_id = ANY(@CourseIds))
              ORDER BY mb.created_at DESC, mb.id DESC
              LIMIT @Limit;
              """
            : """
              SELECT
                  mb.id AS Id,
                  mb.course_id AS CourseId,
                  mb.target_entity_type AS TargetType,
                  mb.target_entity_id AS TargetId,
                  mb.created_at AS CreatedAt,
                  (
                      SELECT COUNT(*)
                      FROM material_bookmarks
                      WHERE user_id = @UserId
                        AND (@CourseIds::uuid[] IS NULL OR course_id = ANY(@CourseIds))
                  ) AS TotalCount
              FROM material_bookmarks mb
              WHERE mb.user_id = @UserId
                AND (@CourseIds::uuid[] IS NULL OR mb.course_id = ANY(@CourseIds))
                AND (mb.created_at, mb.id) < (@CursorCreatedAt, @CursorId)
              ORDER BY mb.created_at DESC, mb.id DESC
              LIMIT @Limit;
              """;

        List<BookmarkIdRow> rows = (await connection.QueryAsync<BookmarkIdRow>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = _user.UserId,
                    CourseIds = courseIdsFilter,
                    CursorCreatedAt = cursor?.EnrolledAt,
                    CursorId = cursor?.LastId,
                    Limit = query.Limit + 1
                },
                cancellationToken: cancellationToken))).ToList();

        long totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;
        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        IReadOnlyList<BookmarkIdDto> items = rows
            .Select(r => new BookmarkIdDto(
                r.CourseId,
                new EntityReferenceDto(r.TargetType, r.TargetId),
                r.CreatedAt))
            .ToArray();

        string? nextCursor = hasMore
            ? Cursor.Encode(rows[^1].CreatedAt, rows[^1].Id)
            : null;

        return new CursorResponse<BookmarkIdDto>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount
        };
    }

    private sealed class BookmarkIdRow
    {
        public Guid Id { get; init; }
        public Guid CourseId { get; init; }
        public EntityType TargetType { get; init; }
        public Guid TargetId { get; init; }
        public DateTime CreatedAt { get; init; }
        public long TotalCount { get; init; }
    }
}
