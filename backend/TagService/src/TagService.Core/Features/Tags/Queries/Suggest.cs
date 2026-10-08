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
using TagService.Contracts.Tags.Dtos;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.Queries;

file sealed record SuggestTagRow(Guid Id, string Title, string Slug, string Kind);

public sealed class SuggestTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/suggest", async Task<EndpointResult<PaginationResponse<TagDto>>> (
            [AsParameters] SuggestTagsRequest request,
            [FromServices] SuggestTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new SuggestTagsQuery(request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class SuggestTagsValidator : AbstractValidator<SuggestTagsQuery>
{
    public SuggestTagsValidator()
    {
        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(Error.Validation("tags.suggest.page", "Номер страницы должен быть больше 0"));

        RuleFor(x => x.Request.PageSize)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MEDIUM_MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.suggest.pagesize", "Размер страницы должен быть от 1 до 50"));

        RuleFor(x => x.Request.Search)
            .MaximumLength(Constants.MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.suggest.search", "Поиск должен быть не длиннее 50 символов"));
    }
}

public sealed record SuggestTagsQuery(SuggestTagsRequest Request) : IQuery;

public sealed class SuggestTagsHandler : IQueryHandlerWithResult<PaginationResponse<TagDto>, SuggestTagsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<SuggestTagsQuery> _validator;

    public SuggestTagsHandler(ITransactionManager transactionManager, IValidator<SuggestTagsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<PaginationResponse<TagDto>, Error>> Handle(
        SuggestTagsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        List<string> conditionals =
        [
            "t.kind = @Kind"
        ];

        DynamicParameters parameters = new();

        string? search = query.Request.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditionals.Add("t.title ILIKE @Search || '%'");
            parameters.Add("Search", search, DbType.String);
        }

        if (query.Request.AuthorId is not null)
        {
            conditionals.Add("t.author_id = @AuthorId");
            parameters.Add("AuthorId", query.Request.AuthorId.Value, DbType.Guid);
        }

        parameters.Add("PageSize", query.Request.PageSize, DbType.Int32);
        parameters.Add("Page", (query.Request.Page - 1) * query.Request.PageSize, DbType.Int32);
        parameters.Add("Kind", TagKind.CANON.ToString(), DbType.String);

        string whereClause = "WHERE " + string.Join(" AND ", conditionals);

        string sql = $"""
            SELECT COUNT(*)
            FROM tags t
            {whereClause};

            SELECT
                t.id,
                t.title,
                t.slug,
                t.kind
            FROM tags t
            {whereClause}
            ORDER BY t.title
            LIMIT @PageSize OFFSET @Page;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        long totalCount = await grid.ReadFirstAsync<long>();
        List<SuggestTagRow> rows = (await grid.ReadAsync<SuggestTagRow>()).ToList();

        TagDto[] items = rows
            .Select(row => new TagDto
            {
                Id = row.Id,
                Title = row.Title,
                Slug = row.Slug,
                Kind = Enum.Parse<TagKind>(row.Kind).ToKindString()
            })
            .ToArray();

        int totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.Request.PageSize);

        return new PaginationResponse<TagDto>(
            items,
            (int)totalCount,
            query.Request.Page,
            query.Request.PageSize,
            totalPages);
    }
}
