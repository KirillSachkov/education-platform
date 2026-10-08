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

file sealed record TagBatchRow(Guid Id, string Title, string Slug, string Kind);

public sealed class GetTagsBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/batch", async Task<EndpointResult<IReadOnlyList<TagDto>>> (
            [AsParameters] GetTagsBatchRequest request,
            [FromServices] GetTagsBatchHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetTagsBatchQuery(request), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetTagsBatchValidator : AbstractValidator<GetTagsBatchQuery>
{
    public GetTagsBatchValidator()
    {
        RuleFor(x => x.Request.TagIds)
            .Must(x => x.Length > 0)
            .WithError(Error.Validation("tags.batch.empty", "Необходимо указать хотя бы один идентификатор тега"))
            .Must(x => x.Length <= 200)
            .WithError(Error.Validation("tags.batch.too_many", "Допустимо не более 200 идентификаторов тегов за один запрос"))
            .Must(x => x.Length == x.Distinct().Count())
            .WithError(Error.Validation("tags.batch.duplicates", "Идентификаторы тегов должны быть уникальными"));
    }
}

public sealed record GetTagsBatchQuery(GetTagsBatchRequest Request) : IQuery;

public sealed class GetTagsBatchHandler : IQueryHandlerWithResult<IReadOnlyList<TagDto>, GetTagsBatchQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetTagsBatchQuery> _validator;

    public GetTagsBatchHandler(ITransactionManager transactionManager, IValidator<GetTagsBatchQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<TagDto>, Error>> Handle(
        GetTagsBatchQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        List<string> conditionals =
        [
            "t.id = ANY(@TagIds)"
        ];

        DynamicParameters parameters = new();
        parameters.Add("TagIds", query.Request.TagIds);

        if (query.Request.AuthorId is not null)
        {
            conditionals.Add("t.author_id = @AuthorId");
            parameters.Add("AuthorId", query.Request.AuthorId.Value);
        }

        string whereClause = "WHERE " + string.Join(" AND ", conditionals);

        string sql = $"""
            SELECT
                t.id,
                t.title,
                t.slug,
                t.kind
            FROM tags t
            {whereClause};
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IReadOnlyList<TagDto> items = (await connection.QueryAsync<TagBatchRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)))
            .Select(row => new TagDto
            {
                Id = row.Id,
                Title = row.Title,
                Slug = row.Slug,
                Kind = Enum.Parse<TagKind>(row.Kind).ToKindString()
            })
            .ToList();

        return Result.Success<IReadOnlyList<TagDto>, Error>(items);
    }
}
