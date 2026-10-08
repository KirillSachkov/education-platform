using System.Data.Common;
using Common;
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

public sealed record GetEntitiesTagsSearchLookupBatchQuery(GetEntitiesTagsSearchLookupRequest Request) : IQuery;

public sealed class GetEntitiesTagsSearchLookupBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/search/entities-tags/batch", async Task<EndpointResult<IReadOnlyList<EntityTagsSearchLookupBatchDto>>> (
                [FromBody] GetEntitiesTagsSearchLookupRequest request,
                [FromServices] GetEntitiesTagsSearchLookupBatchHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetEntitiesTagsSearchLookupBatchQuery(request), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

sealed record EntityTagsSearchLookupBatchRow(
    EntityType EntityType,
    Guid EntityId,
    Guid TagId,
    string TagTitle);

public sealed class GetEntitiesTagsSearchLookupBatchValidator
    : AbstractValidator<GetEntitiesTagsSearchLookupBatchQuery>
{
    public GetEntitiesTagsSearchLookupBatchValidator()
    {
        RuleFor(x => x.Request.Entities)
            .Must(x => x.Count > 0)
            .WithError(Error.Validation("tags.search_lookup.entities.empty", "Необходимо указать хотя бы одну сущность"))
            .Must(x => x.Count <= Constants.MAX_SEARCH_LOOKUP_BATCH_SIZE)
            .WithError(Error.Validation(
                "tags.search_lookup.entities.too_many",
                $"Допустимо не более {Constants.MAX_SEARCH_LOOKUP_BATCH_SIZE} сущностей за один запрос"))
            .Must(static entities => entities
                .Select(static entity => (entity.EntityType, entity.EntityId))
                .Distinct()
                .Count() == entities.Count)
            .WithError(Error.Validation("tags.search_lookup.entities.duplicates", "Список сущностей не должен содержать дубликаты"));

        RuleForEach(x => x.Request.Entities)
            .ChildRules(entity =>
            {
                entity.RuleFor(x => x.EntityType)
                    .IsInEnum()
                    .WithError(Error.Validation("tags.search_lookup.entity_type.invalid", "Тип сущности не поддерживается"));
            });
    }
}

public sealed class GetEntitiesTagsSearchLookupBatchHandler
    : IQueryHandlerWithResult<IReadOnlyList<EntityTagsSearchLookupBatchDto>, GetEntitiesTagsSearchLookupBatchQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetEntitiesTagsSearchLookupBatchQuery> _validator;

    public GetEntitiesTagsSearchLookupBatchHandler(
        ITransactionManager transactionManager,
        IValidator<GetEntitiesTagsSearchLookupBatchQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>> Handle(
        GetEntitiesTagsSearchLookupBatchQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        EntityTagsSearchLookupBatchItem[] requestedEntities = query.Request.Entities.ToArray();

        const string sql = """
            WITH requested AS (
                SELECT DISTINCT
                    entity_id,
                    entity_type
                FROM unnest(@EntityIds::uuid[], @EntityTypes::text[]) AS requested(entity_id, entity_type)
            )
            SELECT
                requested.entity_type AS entity_type,
                requested.entity_id AS entity_id,
                t.id AS tag_id,
                t.title AS tag_title
            FROM requested
            JOIN entity_tags et
                ON et.entity_id = requested.entity_id
               AND et.entity_type = requested.entity_type
            JOIN tags t
                ON t.id = et.tag_id
            ORDER BY requested.entity_type, requested.entity_id, t.title;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        EntityTagsSearchLookupBatchRow[] rows = (await connection.QueryAsync<EntityTagsSearchLookupBatchRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        EntityIds = requestedEntities.Select(static entity => entity.EntityId).ToArray(),
                        EntityTypes = requestedEntities
                            .Select(static entity => ResourceTypes.FromEntityType(entity.EntityType))
                            .ToArray(),
                    },
                    cancellationToken: cancellationToken)))
            .ToArray();

        IReadOnlyList<EntityTagsSearchLookupBatchDto> result = rows
            .GroupBy(static row => new { row.EntityType, row.EntityId })
            .Select(static group => new EntityTagsSearchLookupBatchDto(
                group.Key.EntityType,
                group.Key.EntityId,
                group.Select(static row => row.TagId).ToArray(),
                group.Select(static row => row.TagTitle).ToArray()))
            .ToList();

        return Result.Success<IReadOnlyList<EntityTagsSearchLookupBatchDto>, Error>(result);
    }
}
