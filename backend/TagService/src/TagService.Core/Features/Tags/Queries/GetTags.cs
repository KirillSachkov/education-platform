using System.Data;
using System.Data.Common;
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
using TagService.Contracts.Tags;
using TagService.Contracts.Tags.Dtos;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.Queries;

file sealed record TagPageRow(Guid Id, string Title, string Slug, string Kind);

public sealed class GetTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags", async Task<EndpointResult<CursorResponse<TagDto>>> (
            [AsParameters] GetTagsRequest request,
            [FromServices] GetTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetTagsQuery(request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetTagsValidator : AbstractValidator<GetTagsQuery>
{
    public GetTagsValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MEDIUM_MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.tags.limit", "Размер страницы должен быть от 1 до 50"));

        RuleFor(x => x.Request.Search)
            .MaximumLength(Constants.MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.tags.search", "Поиск должен быть не длиннее 50 символов"));

        RuleFor(x => x.Request.Kind)
            .Must(static kind => string.IsNullOrWhiteSpace(kind)
                || Enum.TryParse<TagKind>(kind.Trim(), ignoreCase: true, out _))
            .WithError(Error.Validation("tags.tags.kind", "Неизвестный вид тега"));

        RuleFor(x => x.Request.Cursor)
            .Must(cursor => string.IsNullOrWhiteSpace(cursor)
                || (cursor.Length <= Constants.MAX_CURSOR_LENGTH && TagCursor.Decode(cursor) is not null))
            .WithError(Error.Validation("tags.tags.cursor", "Некорректный курсор пагинации"));
    }
}

public sealed record GetTagsQuery(GetTagsRequest Request) : IQuery;

public sealed class GetTagsHandler : IQueryHandlerWithResult<CursorResponse<TagDto>, GetTagsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetTagsQuery> _validator;

    public GetTagsHandler(ITransactionManager transactionManager, IValidator<GetTagsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<TagDto>, Error>> Handle(
        GetTagsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Filter conditions EXCLUDING cursor — used for both total count and page fetch.
        List<string> filterConditionals = [];
        DynamicParameters parameters = new();

        string? search = query.Request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filterConditionals.Add("t.title ILIKE @Search || '%'");
            parameters.Add("Search", search, DbType.String);
        }

        if (query.Request.Kind is not null)
        {
            filterConditionals.Add("t.kind = @Status");
            parameters.Add("Status", query.Request.Kind.Trim().ToUpperInvariant(), DbType.String);
        }

        if (query.Request.AuthorId is not null)
        {
            filterConditionals.Add("t.author_id = @AuthorId");
            parameters.Add("AuthorId", query.Request.AuthorId.Value, DbType.Guid);
        }

        string filterWhere = filterConditionals.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", filterConditionals);

        // Page-level conditions = filter + cursor (cursor narrows what we fetch, not the total).
        List<string> pageConditionals = [.. filterConditionals];
        TagCursor? cursor = TagCursor.Decode(query.Request.Cursor);
        if (cursor is not null)
        {
            pageConditionals.Add("(t.title, t.id) > (@CursorTitle, @CursorId)");
            parameters.Add("CursorTitle", cursor.Title, DbType.String);
            parameters.Add("CursorId", cursor.LastId, DbType.Guid);
        }

        parameters.Add("Limit", query.Request.Limit + 1, DbType.Int32);

        string pageWhere = pageConditionals.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", pageConditionals);

        // Split count + page into one round-trip via QueryMultipleAsync.
        // filterWhere is the no-cursor filter (drives total); pageWhere adds the cursor for the slice.
        string sql = $"""
            SELECT COUNT(*) FROM tags t {filterWhere};

            SELECT
                t.id,
                t.title,
                t.slug,
                t.kind
            FROM tags t
            {pageWhere}
            ORDER BY t.title, t.id
            LIMIT @Limit;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        long totalCount = await grid.ReadFirstAsync<long>();
        List<TagPageRow> rows = (await grid.ReadAsync<TagPageRow>()).ToList();

        bool hasMore = rows.Count > query.Request.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        TagDto[] items = rows
            .Select(row => new TagDto
            {
                Id = row.Id,
                Title = row.Title,
                Slug = row.Slug,
                Kind = Enum.Parse<TagKind>(row.Kind).ToKindString(),
            })
            .ToArray();

        string? nextCursor = hasMore && items.Length > 0
            ? TagCursor.Encode(items[^1].Title, items[^1].Id)
            : null;

        return new CursorResponse<TagDto>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }
}
