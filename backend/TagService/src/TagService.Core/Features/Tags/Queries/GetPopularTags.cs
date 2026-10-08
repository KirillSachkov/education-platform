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
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using TagService.Contracts.Tags.Dtos;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.Queries;

file sealed record PopularTagRow(Guid Id, string Title, string Slug, string Kind, int UsageCount);

public sealed class GetPopularTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/popular", async Task<EndpointResult<IReadOnlyList<TagDto>>> (
            [AsParameters] GetPopularTagsRequest request,
            [FromServices] GetPopularTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetPopularTagsQuery(request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetPopularTagsValidator : AbstractValidator<GetPopularTagsQuery>
{
    public GetPopularTagsValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(1, 20)
            .WithError(Error.Validation("tags.popular.limit", "Лимит популярных тегов должен быть от 1 до 20"));
    }
}

public sealed record GetPopularTagsQuery(GetPopularTagsRequest Request) : IQuery;

public sealed class GetPopularTagsHandler : IQueryHandlerWithResult<IReadOnlyList<TagDto>, GetPopularTagsQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetPopularTagsQuery> _validator;
    private readonly HybridCache _cache;

    public GetPopularTagsHandler(
        ITransactionManager transactionManager,
        IValidator<GetPopularTagsQuery> validator,
        HybridCache cache)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<TagDto>, Error>> Handle(
        GetPopularTagsQuery query,
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
        parameters.Add("Kind", TagKind.CANON.ToString(), DbType.String);
        parameters.Add("Limit", query.Request.Limit, DbType.Int32);

        if (query.Request.AuthorId is not null)
        {
            conditionals.Add("t.author_id = @AuthorId");
            parameters.Add("AuthorId", query.Request.AuthorId.Value, DbType.Guid);
        }

        string whereClause = "WHERE " + string.Join(" AND ", conditionals);

        string cacheKey = $"tags:popular:{query.Request.AuthorId?.ToString("N") ?? "all"}:{query.Request.Limit}";

        IReadOnlyList<TagDto> cached = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                string sql = $"""
                    SELECT
                        t.id,
                        t.title,
                        t.slug,
                        t.kind,
                        COUNT(et.id)::int AS usage_count
                    FROM tags t
                    JOIN entity_tags et ON et.tag_id = t.id
                    -- Popularity is intentionally global across all entity types:
                    -- callers want the most-used canonical tags overall, not per-resource ranking.
                    {whereClause}
                    GROUP BY t.id, t.title, t.slug, t.kind
                    ORDER BY usage_count DESC, t.title
                    LIMIT @Limit;
                    """;

                DbConnection connection = _transactionManager.GetDbConnection();

                List<PopularTagRow> rows = (await connection.QueryAsync<PopularTagRow>(
                        new CommandDefinition(sql, parameters, cancellationToken: ct)))
                    .ToList();

                return rows
                    .Select(row => new TagDto
                    {
                        Id = row.Id,
                        Title = row.Title,
                        Slug = row.Slug,
                        Kind = Enum.Parse<TagKind>(row.Kind).ToKindString()
                    })
                    .ToArray();
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        return Result.Success<IReadOnlyList<TagDto>, Error>(cached);
    }
}
