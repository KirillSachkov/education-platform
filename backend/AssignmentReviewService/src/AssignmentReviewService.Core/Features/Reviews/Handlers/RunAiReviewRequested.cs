using AssignmentReviewService.Core.Features.Reviews.UseCases;
using Core.Abstractions;

namespace AssignmentReviewService.Core.Features.Reviews.Handlers;

/// <summary>
///     Локальное (in-process) сообщение: запросить выполнение AI-итерации.
///     Единая команда для обоих flow'ов:
///     <list type="bullet">
///         <item>Auto: <see cref="IssueSubmissionAwaitingReviewHandler"/> публикует её
///         после создания <see cref="AssignmentReviewService.Domain.Reviews.AiReview"/>
///         под новый submission.</item>
///         <item>Manual: HTTP endpoint <c>POST /run-iteration/</c> публикует её
///         после fail-fast authz/state-проверок и сразу возвращает <c>202 Accepted</c>
///         (issue #357 — синхронный sync-call ломал iteration при разрыве клиента).</item>
///     </list>
///     Доставляется через durable outbox → попадает в Wolverine-handler
///     <see cref="RunAiReviewRequestedHandler"/> в отдельном message-execution scope'е,
///     не связанном с HTTP-запросом юзера. <c>DefaultExecutionTimeout=2h</c> (#337)
///     обеспечивает потолок execution'а; реальный per-LLM-call лимит навязывает
///     reviewer-slot <c>TimeoutSeconds</c> (CancelAfter).
/// </summary>
/// <param name="AiReviewId">Идентификатор существующего <c>AiReview</c> aggregate'а.</param>
/// <param name="ModelOverride">
///     Optional model override (admin-only, гейтится permission'ом
///     <c>PlatformPermissions.Ai.OVERRIDE_MODEL</c> в <see cref="RunIterationHandler"/>).
///     Эndpoint валидирует permission ДО publish'а; auto-flow всегда передаёт <c>null</c>.
/// </param>
public sealed record RunAiReviewRequested(
    Guid AiReviewId,
    string? ModelOverride,
    bool AllowOversizedDiff = false,
    bool ForceFresh = false);

/// <summary>
///     Wolverine-handler: выполняет AI-итерацию в worker-scope'е, не связанном с
///     HTTP-запросом инициатора. Делегирует в <see cref="RunIterationHandler"/> с
///     <c>SystemTriggered=true</c> — HTTP-authz пропускаем, потому что endpoint
///     уже сделал её до публикации команды (для auto-flow review гарантированно
///     принадлежит студенту-сабмиттеру). Best-effort: ошибка итерации уже записана
///     handler'ом в БД как FAILED, юзер видит её через polling /by-submission.
/// </summary>
public sealed class RunAiReviewRequestedHandler
{
    private readonly ICommandHandler<RunIterationResponse, RunIterationCommand> _runIteration;
    private readonly ILogger<RunAiReviewRequestedHandler> _logger;

    public RunAiReviewRequestedHandler(
        ICommandHandler<RunIterationResponse, RunIterationCommand> runIteration,
        ILogger<RunAiReviewRequestedHandler> logger)
    {
        _runIteration = runIteration;
        _logger = logger;
    }

    public async Task HandleAsync(RunAiReviewRequested message, CancellationToken ct)
    {
        var result = await _runIteration.Handle(
            new RunIterationCommand(message.AiReviewId, message.ModelOverride, SystemTriggered: true,
                AllowOversizedDiff: message.AllowOversizedDiff, ForceFresh: message.ForceFresh), ct);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Run AI review {AiReviewId} failed: {Code}",
                message.AiReviewId,
                result.Error.Messages[0].Code);
        }
    }
}
