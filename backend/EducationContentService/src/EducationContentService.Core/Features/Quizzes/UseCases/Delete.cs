using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Core.Database;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Quizzes.UseCases;

public sealed record DeleteQuizCommand(Guid QuizId) : ICommand;

public sealed class DeleteQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("quizzes/{quizId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid quizId,
                    [FromServices] DeleteQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteQuizCommand(quizId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Hard-delete квиза с каскадом по ссылкам (#489): обнуляет <c>materials.quiz_id</c>
///     (материалы живут дальше без блока «Проверь себя»), сносит привязки
///     <c>course_quizzes</c>, QUIZ-элементы подборок <c>collection_items</c> (#491,
///     generic-ссылка без FK — без cleanup'а остались бы orphan-строки) и Quiz-элементы
///     модулей <c>module_items</c> (#492) — в одной транзакции с удалением самого квиза.
///     Публикует <c>quiz.hard_deleted</c> (#490) — self-consume handler чистит
///     Redis-теги доступа.
/// </summary>
public sealed class DeleteQuizHandler : ICommandHandler<Guid, DeleteQuizCommand>
{
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<DeleteQuizHandler> _logger;

    public DeleteQuizHandler(
        IQuizzesRepository quizzesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        UserScopedData userScopedData,
        ILogger<DeleteQuizHandler> logger)
    {
        _quizzesRepository = quizzesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        DeleteQuizCommand command,
        CancellationToken cancellationToken)
    {
        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == command.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(command.QuizId);

        Quiz quiz = quizResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        // Атомарно: обнуление ссылок + удаление привязок + удаление квиза — либо всё, либо ничего
        // (паттерн DeleteMaterialHandler). Без явного Begin/Commit Dapper-стейтменты прошли бы
        // в autocommit и при падении SaveChangesAsync остались бы закоммиченными.
        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        DbConnection connection = _transactionManager.GetDbConnection();
        const string cascadeSql = """
            UPDATE materials
            SET quiz_id = NULL
            WHERE quiz_id = @QuizId;

            DELETE FROM course_quizzes
            WHERE quiz_id = @QuizId;

            DELETE FROM collection_items
            WHERE item_type = 'QUIZ' AND reference_id = @QuizId;

            DELETE FROM module_items
            WHERE item_type = 'Quiz' AND reference_id = @QuizId;
            """;
        await connection.ExecuteAsync(
            new CommandDefinition(
                cascadeSql,
                new { QuizId = quiz.Id },
                cancellationToken: cancellationToken));

        _quizzesRepository.Delete(quiz);

        await _outbox.PublishAsync(new QuizHardDeleted(quiz.Id));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        _logger.LogInformation(
            "Quiz {QuizId} hard-deleted (material refs nullified, course bindings + collection items + module items removed)",
            quiz.Id);

        return quiz.Id;
    }
}
