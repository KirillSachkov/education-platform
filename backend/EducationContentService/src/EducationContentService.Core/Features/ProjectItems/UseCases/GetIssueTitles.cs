using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Issues;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

/// <summary>
///     Internal batch endpoint — отдаёт title'ы задач по списку ID. Используется
///     ProgressService'ом для enrichment'а review-feed'а.
/// </summary>
public sealed record GetIssueTitlesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetIssueTitlesQueryValidator : AbstractValidator<GetIssueTitlesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetIssueTitlesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetIssueTitlesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/issues/titles",
                async Task<EndpointResult<IReadOnlyList<IssueTitleDto>>> (
                    [FromBody] GetIssueTitlesRequest request,
                    [FromServices] GetIssueTitlesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetIssueTitlesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetIssueTitlesHandler
    : IQueryHandlerWithResult<IReadOnlyList<IssueTitleDto>, GetIssueTitlesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetIssueTitlesQuery> _validator;

    public GetIssueTitlesHandler(
        ITransactionManager transactionManager,
        IValidator<GetIssueTitlesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<IssueTitleDto>, Error>> Handle(
        GetIssueTitlesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        const string sql = """
                           SELECT id AS IssueId, title AS Title
                           FROM issues
                           WHERE id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<IssueTitleDto> rows = await connection.QueryAsync<IssueTitleDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<IssueTitleDto>, Error>(rows.ToList());
    }
}
