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

file sealed record TagAliasPageRow(Guid Id, string Title, string Slug, string Kind);

public sealed class GetTagAliasesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/{id:guid}/aliases", async Task<EndpointResult<PaginationResponse<TagDto>>> (
            [FromRoute] Guid id,
            [AsParameters] GetTagAliasesRequest request,
            [FromServices] GetTagAliasesHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetTagAliasesQuery(id, request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetTagAliasesValidator : AbstractValidator<GetTagAliasesQuery>
{
    public GetTagAliasesValidator()
    {
        RuleFor(x => x.TagId)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("tagId"));

        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(Error.Validation("tags.aliases.page", "Номер страницы должен быть больше 0"));

        RuleFor(x => x.Request.PageSize)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MEDIUM_MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.aliases.pagesize", "Размер страницы должен быть от 1 до 50"));

        RuleFor(x => x.Request.Search)
            .MaximumLength(Constants.MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.aliases.search", "Поиск должен быть не длиннее 50 символов"));
    }
}

public sealed record GetTagAliasesQuery(Guid TagId, GetTagAliasesRequest Request) : IQuery;

public sealed class GetTagAliasesHandler : IQueryHandlerWithResult<PaginationResponse<TagDto>, GetTagAliasesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetTagAliasesQuery> _validator;

    public GetTagAliasesHandler(ITransactionManager transactionManager, IValidator<GetTagAliasesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<PaginationResponse<TagDto>, Error>> Handle(
        GetTagAliasesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        List<string> conditionals =
        [
            "ta.tag_id = @TagId"
        ];

        DynamicParameters parameters = new();

        string? search = query.Request.Search?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditionals.Add("alias_tag.title ILIKE @Search || '%'");
            parameters.Add("Search", search, DbType.String);
        }

        parameters.Add("TagId", query.TagId, DbType.Guid);
        parameters.Add("PageSize", query.Request.PageSize, DbType.Int32);
        parameters.Add("Page", (query.Request.Page - 1) * query.Request.PageSize, DbType.Int32);

        string whereClause = "WHERE " + string.Join(" AND ", conditionals);

        string sql = $"""
            SELECT COUNT(*)
            FROM tag_aliases ta
            INNER JOIN tags alias_tag ON alias_tag.id = ta.alias_tag_id
            {whereClause};

            SELECT
                alias_tag.id,
                alias_tag.title,
                alias_tag.slug,
                alias_tag.kind
            FROM tag_aliases ta
            INNER JOIN tags alias_tag ON alias_tag.id = ta.alias_tag_id
            {whereClause}
            ORDER BY alias_tag.title
            LIMIT @PageSize OFFSET @Page;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        long totalCount = await grid.ReadFirstAsync<long>();
        List<TagAliasPageRow> rows = (await grid.ReadAsync<TagAliasPageRow>()).ToList();

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
