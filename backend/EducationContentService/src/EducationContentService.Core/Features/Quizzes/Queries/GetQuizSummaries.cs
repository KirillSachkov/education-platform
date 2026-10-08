using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Quizzes;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Quizzes.Queries;

/// <summary>
///     Internal batch endpoint — отдаёт summary квизов (title + purpose + представительный
///     курс) по списку ID. Используется ProgressService'ом для enrichment'а страницы
///     «Мои тесты» и админ-статистикой по тестам (#556).
/// </summary>
public sealed record GetQuizSummariesQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetQuizSummariesQueryValidator : AbstractValidator<GetQuizSummariesQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetQuizSummariesQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetQuizSummariesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/quizzes/summaries",
                async Task<EndpointResult<IReadOnlyList<QuizSummaryLookupDto>>> (
                    [FromBody] GetQuizSummariesRequest request,
                    [FromServices] GetQuizSummariesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetQuizSummariesQuery(request.Ids), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetQuizSummariesHandler
    : IQueryHandlerWithResult<IReadOnlyList<QuizSummaryLookupDto>, GetQuizSummariesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetQuizSummariesQuery> _validator;

    public GetQuizSummariesHandler(
        ITransactionManager transactionManager,
        IValidator<GetQuizSummariesQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> Handle(
        GetQuizSummariesQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        // Один представительный курс на квиз — наименьший course_id.
        // PostgreSQL НЕ имеет агрегата MIN(uuid) (падает «function min(uuid) does
        // not exist» на plan-этапе, при любых данных), поэтому representative-курс
        // берём через DISTINCT ON ... ORDER BY вместо MIN (#556 fix).
        // Standalone-квиз (нет привязок) → CourseId = NULL (LEFT JOIN).
        const string sql = """
                           SELECT
                               q.id AS Id,
                               q.title AS Title,
                               q.purpose AS Purpose,
                               cq.course_id AS CourseId
                           FROM quizzes q
                           LEFT JOIN (
                               SELECT DISTINCT ON (quiz_id) quiz_id, course_id
                               FROM course_quizzes
                               ORDER BY quiz_id, course_id
                           ) cq ON cq.quiz_id = q.id
                           WHERE q.id = ANY(@Ids);
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<QuizSummaryLookupDto> rows = await connection.QueryAsync<QuizSummaryLookupDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<QuizSummaryLookupDto>, Error>(rows.ToList());
    }
}
