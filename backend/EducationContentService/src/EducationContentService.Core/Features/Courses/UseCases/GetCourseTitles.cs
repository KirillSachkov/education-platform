using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Courses;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.UseCases;

/// <summary>
///     Internal batch endpoint — отдаёт title'ы курсов по списку ID. Используется
///     ProgressService'ом для enrichment'а review-feed'а.
/// </summary>
public sealed record GetCourseTitlesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetCourseTitlesQueryValidator : AbstractValidator<GetCourseTitlesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetCourseTitlesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetCourseTitlesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/courses/titles",
                async Task<EndpointResult<IReadOnlyList<CourseTitleDto>>> (
                    [FromBody] GetCourseTitlesRequest request,
                    [FromServices] GetCourseTitlesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseTitlesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetCourseTitlesHandler
    : IQueryHandlerWithResult<IReadOnlyList<CourseTitleDto>, GetCourseTitlesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetCourseTitlesQuery> _validator;

    public GetCourseTitlesHandler(
        ITransactionManager transactionManager,
        IValidator<GetCourseTitlesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<CourseTitleDto>, Error>> Handle(
        GetCourseTitlesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        const string sql = """
                           SELECT id AS CourseId, title AS Title, slug AS Slug, kind AS Kind
                           FROM courses
                           WHERE id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<CourseTitleDto> rows = await connection.QueryAsync<CourseTitleDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<CourseTitleDto>, Error>(rows.ToList());
    }
}
