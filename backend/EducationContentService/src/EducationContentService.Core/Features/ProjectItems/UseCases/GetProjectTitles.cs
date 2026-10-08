using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Projects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

/// <summary>
///     Internal batch endpoint — отдаёт title'ы проектов по списку ID. Используется
///     ProgressService'ом для enrichment'а review-feed'а.
/// </summary>
public sealed record GetProjectTitlesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetProjectTitlesQueryValidator : AbstractValidator<GetProjectTitlesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetProjectTitlesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetProjectTitlesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/projects/titles",
                async Task<EndpointResult<IReadOnlyList<ProjectTitleDto>>> (
                    [FromBody] GetProjectTitlesRequest request,
                    [FromServices] GetProjectTitlesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetProjectTitlesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetProjectTitlesHandler
    : IQueryHandlerWithResult<IReadOnlyList<ProjectTitleDto>, GetProjectTitlesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetProjectTitlesQuery> _validator;

    public GetProjectTitlesHandler(
        ITransactionManager transactionManager,
        IValidator<GetProjectTitlesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> Handle(
        GetProjectTitlesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        const string sql = """
                           SELECT id AS ProjectId, title AS Title
                           FROM projects
                           WHERE id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<ProjectTitleDto> rows = await connection.QueryAsync<ProjectTitleDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<ProjectTitleDto>, Error>(rows.ToList());
    }
}
