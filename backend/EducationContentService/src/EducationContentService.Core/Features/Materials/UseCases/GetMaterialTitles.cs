using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Materials.UseCases;

/// <summary>
///     Internal batch endpoint — отдаёт title'ы материалов по списку ID. Заменяет
///     прямой cross-schema SELECT из CommentService, чтобы service-bounaries не
///     протекали через SQL.
/// </summary>
public sealed record GetMaterialTitlesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetMaterialTitlesQueryValidator : AbstractValidator<GetMaterialTitlesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetMaterialTitlesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetMaterialTitlesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/materials/titles",
                async Task<EndpointResult<IReadOnlyList<MaterialTitleDto>>> (
                    [FromBody] GetMaterialTitlesRequest request,
                    [FromServices] GetMaterialTitlesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialTitlesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialTitlesHandler
    : IQueryHandlerWithResult<IReadOnlyList<MaterialTitleDto>, GetMaterialTitlesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetMaterialTitlesQuery> _validator;

    public GetMaterialTitlesHandler(
        ITransactionManager transactionManager,
        IValidator<GetMaterialTitlesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> Handle(
        GetMaterialTitlesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        const string sql = """
                           SELECT id AS MaterialId, title AS Title
                           FROM materials
                           WHERE id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<MaterialTitleDto> rows = await connection.QueryAsync<MaterialTitleDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<MaterialTitleDto>, Error>(rows.ToList());
    }
}
