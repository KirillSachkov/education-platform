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
using TagService.Domain.EntityTags;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.Queries;

file sealed record EntityTagPageRow(Guid Id, string Title, string Slug, string Kind);

public sealed class GetEntityTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/entity", async Task<EndpointResult<PaginationResponse<TagDto>>> (
            [AsParameters] GetEntityTagsRequest request,
            [FromServices] GetEntityTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetEntityTagsQuery(request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetEntityTagsValidator : AbstractValidator<GetEntityTagsQuery>
{
    public GetEntityTagsValidator()
    {
        RuleFor(x => x.Request)
            .MustBeValueObject(x => TagEntityReference.Of(x.EntityType, x.EntityId));

        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(Error.Validation("tags.entity-tags.page", "Номер страницы должен быть больше 0"));

        RuleFor(x => x.Request.PageSize)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MEDIUM_MAX_PAGE_LENGTH)
            .WithError(Error.Validation("tags.entity-tags.pagesize", "Размер страницы должен быть от 1 до 50"));
    }
}

public sealed record GetEntityTagsQuery(GetEntityTagsRequest Request) : IQuery;

public sealed class GetEntityTagsHandler : IQueryHandlerWithResult<PaginationResponse<TagDto>, GetEntityTagsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetEntityTagsQuery> _validator;

    public GetEntityTagsHandler(ITransactionManager transactionManager, IValidator<GetEntityTagsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<PaginationResponse<TagDto>, Error>> Handle(
        GetEntityTagsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        const string sql = """
            SELECT COUNT(*)
            FROM entity_tags links
            WHERE links.entity_type = @EntityType
              AND links.entity_id = @EntityId;

            SELECT
                t.id,
                t.title,
                t.slug,
                t.kind
            FROM entity_tags links
            INNER JOIN tags t ON t.id = links.tag_id
            WHERE links.entity_type = @EntityType
              AND links.entity_id = @EntityId
            ORDER BY t.title
            LIMIT @PageSize OFFSET @Page;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();
        TagEntityReference entityReference = TagEntityReference.Of(
            query.Request.EntityType,
            query.Request.EntityId).Value;
        var parameters = new
        {
            EntityType = entityReference.Type.ToString().ToLowerInvariant(),
            EntityId = entityReference.Id,
            query.Request.PageSize,
            Page = (query.Request.Page - 1) * query.Request.PageSize
        };
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        long totalCount = await grid.ReadFirstAsync<long>();
        List<EntityTagPageRow> rows = (await grid.ReadAsync<EntityTagPageRow>()).ToList();

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
