using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TagService.Contracts.SearchLookup;

namespace TagService.Core.Features.SearchLookup;

public sealed record GetEntityTagsSearchLookupBatchQuery(GetEntityTagsSearchLookupRequest Request) : IQuery;

public sealed class GetEntityTagsSearchLookupBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/search/entity-tags/batch", async Task<EndpointResult<IReadOnlyList<EntityTagsSearchLookupDto>>> (
                [FromBody] GetEntityTagsSearchLookupRequest request,
                [FromServices] GetEntityTagsSearchLookupBatchHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetEntityTagsSearchLookupBatchQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

sealed record EntityTagLookupRow(Guid EntityId, Guid TagId, string TagTitle);

public sealed class GetEntityTagsSearchLookupBatchValidator
    : AbstractValidator<GetEntityTagsSearchLookupBatchQuery>
{
    public GetEntityTagsSearchLookupBatchValidator()
    {
        RuleFor(x => x.Request.EntityType)
            .IsInEnum()
            .WithError(Error.Validation("tags.search_lookup.entity_type.invalid", "Тип сущности не поддерживается"));

        RuleFor(x => x.Request.EntityIds)
            .Must(x => x.Count > 0)
            .WithError(Error.Validation("tags.search_lookup.entity_ids.empty", "Необходимо указать хотя бы один идентификатор сущности"))
            .Must(x => x.Count <= Constants.MAX_SEARCH_LOOKUP_BATCH_SIZE)
            .WithError(Error.Validation(
                "tags.search_lookup.entity_ids.too_many",
                $"Допустимо не более {Constants.MAX_SEARCH_LOOKUP_BATCH_SIZE} идентификаторов сущностей за один запрос"))
            .Must(x => x.Count == x.Distinct().Count())
            .WithError(Error.Validation("tags.search_lookup.entity_ids.duplicates", "Идентификаторы сущностей должны быть уникальными"));
    }
}

public sealed class GetEntityTagsSearchLookupBatchHandler
    : IQueryHandlerWithResult<IReadOnlyList<EntityTagsSearchLookupDto>, GetEntityTagsSearchLookupBatchQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetEntityTagsSearchLookupBatchQuery> _validator;

    public GetEntityTagsSearchLookupBatchHandler(
        ITransactionManager transactionManager,
        IValidator<GetEntityTagsSearchLookupBatchQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<EntityTagsSearchLookupDto>, Error>> Handle(
        GetEntityTagsSearchLookupBatchQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        const string sql = """
            SELECT
                et.entity_id,
                t.id AS tag_id,
                t.title AS tag_title
            FROM entity_tags et
            JOIN tags t
                ON t.id = et.tag_id
            WHERE et.entity_type = @EntityType
              AND et.entity_id = ANY(@EntityIds::uuid[])
            ORDER BY et.entity_id, t.title;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        EntityTagLookupRow[] rows = (await connection.QueryAsync<EntityTagLookupRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        EntityType = ResourceTypes.FromEntityType(query.Request.EntityType),
                        EntityIds = query.Request.EntityIds.ToArray(),
                    },
                    cancellationToken: cancellationToken)))
            .ToArray();

        IReadOnlyList<EntityTagsSearchLookupDto> result = rows
            .GroupBy(static row => row.EntityId)
            .Select(static group => new EntityTagsSearchLookupDto(
                group.Key,
                group.Select(static row => row.TagId).ToArray(),
                group.Select(static row => row.TagTitle).ToArray()))
            .ToList();

        return Result.Success<IReadOnlyList<EntityTagsSearchLookupDto>, Error>(result);
    }
}
