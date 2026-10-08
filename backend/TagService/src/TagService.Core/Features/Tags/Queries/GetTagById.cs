using System.Data.Common;
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
using TagService.Contracts.Tags.Dtos;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.Queries;

public sealed class GetTagByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/tags/{id:guid}", async Task<EndpointResult<TagDto>> (
            [FromRoute] Guid id,
            [FromServices] GetTagByIdHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetTagByIdQuery(id), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY);
    }
}

public sealed class GetTagByIdValidator : AbstractValidator<GetTagByIdQuery>
{
    public GetTagByIdValidator()
    {
        RuleFor(x => x.TagId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("tagId"));
    }
}

public sealed record GetTagByIdQuery(Guid TagId) : IQuery;

public sealed class GetTagByIdHandler : IQueryHandlerWithResult<TagDto, GetTagByIdQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetTagByIdQuery> _validator;

    public GetTagByIdHandler(ITransactionManager transactionManager, IValidator<GetTagByIdQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<TagDto, Error>> Handle(
        GetTagByIdQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        const string sql = """
            SELECT
                t.id,
                t.title,
                t.slug,
                t.kind
            FROM tags t
            WHERE t.id = @TagId
            LIMIT 1;
            """;

        DbConnection connection = _transactionManager.GetDbConnection();

        TagDto? tag = await connection.QuerySingleOrDefaultAsync<TagDto>(sql, new { query.TagId });

        if (tag is null)
            return Error.NotFound("tag.database.notfound", "Тег не найден");

        string kind = Enum.Parse<TagKind>(tag.Kind).ToKindString();

        return tag with { Kind = kind };
    }
}
