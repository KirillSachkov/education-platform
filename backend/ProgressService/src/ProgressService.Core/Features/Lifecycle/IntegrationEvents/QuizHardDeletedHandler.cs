using ProgressService.Core.Abstractions;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Cleanup на <c>quiz.hard_deleted</c> (ST-13 #493): квиз — standalone-сущность,
///     при полном удалении сносим его попытки (<c>quiz_attempts</c>) и пункты прогресса
///     модулей (<c>module_item_progress</c> item_type=QUIZ). Routing: wildcard-binding
///     <c>*.hard_deleted</c> очереди <c>progress.education.lifecycle_events</c> уже
///     покрывает <c>quiz.hard_deleted</c>. LevelTestAttempt'ы намеренно НЕ трогаем —
///     самодостаточные снапшоты, переживают удаление квиза (#479).
/// </summary>
public sealed class QuizHardDeletedHandler
{
    private readonly IQuizAttemptRepository _quizAttemptRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly ILogger<QuizHardDeletedHandler> _logger;

    public QuizHardDeletedHandler(
        IQuizAttemptRepository quizAttemptRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        ILogger<QuizHardDeletedHandler> logger)
    {
        _quizAttemptRepository = quizAttemptRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _logger = logger;
    }

    public async Task Handle(QuizHardDeleted message, CancellationToken cancellationToken)
    {
        int attempts = await _quizAttemptRepository.DeleteByQuizIdAsync(
            message.QuizId, cancellationToken);

        // reference_id уникален кросс-типово (v7 GUID) — фильтр по item_type не нужен,
        // тот же generic-метод используют Material/Issue hard-delete handlers.
        int moduleItems = await _moduleItemProgressRepository.DeleteByReferenceIdAsync(
            message.QuizId, cancellationToken);

        _logger.LogInformation(
            "QuizHardDeleted {QuizId}: deleted {Attempts} quiz attempts, {ModuleItems} module items",
            message.QuizId,
            attempts,
            moduleItems);
    }
}
