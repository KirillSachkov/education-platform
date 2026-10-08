using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

/// <summary>
///     Возвращает все пары (CourseId, ModuleId), в которых квиз размещён через
///     <c>module_items(item_type='Quiz')</c> — зеркало <see cref="GetMaterialCourseContextsHandler"/>.
///     ProgressService использует это, чтобы закрыть <c>module_item_progress</c> во всех
///     enrollment'ах пользователя при passed-попытке квиза (ST-13 #493).
/// </summary>
public sealed record GetQuizModuleContextsQuery(Guid QuizId) : IQuery;

public sealed class GetQuizModuleContextsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/quizzes/{quizId:guid}/module-lookup",
                async Task<EndpointResult<IReadOnlyList<QuizModuleContextDto>>> (
                        [FromRoute] Guid quizId,
                        [FromServices] GetQuizModuleContextsHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetQuizModuleContextsQuery(quizId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetQuizModuleContextsHandler
    : IQueryHandlerWithResult<IReadOnlyList<QuizModuleContextDto>, GetQuizModuleContextsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetQuizModuleContextsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> Handle(
        GetQuizModuleContextsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Квиз может переиспользоваться в нескольких модулях/курсах. Берём только вхождения
        // из module_items — именно для них существует module_item_progress. Запись в
        // course_quizzes без module_items означает «квиз прикреплён к курсу, но не в модуле» —
        // такого blueprint item'а нет, cascade не нужен.
        const string sql = """
                           SELECT
                               ci.course_id AS CourseId,
                               mi.module_id AS ModuleId,
                               (SELECT COUNT(*)::integer FROM module_items mi2 WHERE mi2.module_id = mi.module_id)
                                   AS ModuleItemsTotal
                           FROM module_items mi
                           INNER JOIN course_items ci
                               ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                           WHERE mi.reference_id = @QuizId
                             AND mi.item_type = 'Quiz';
                           """;

        IEnumerable<QuizModuleContextDto> rows = await connection.QueryAsync<QuizModuleContextDto>(
            new CommandDefinition(
                sql,
                new { query.QuizId },
                cancellationToken: cancellationToken));

        IReadOnlyList<QuizModuleContextDto> result = rows.ToList();
        return Result.Success<IReadOnlyList<QuizModuleContextDto>, Error>(result);
    }
}
