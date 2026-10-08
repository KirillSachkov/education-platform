using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.AI;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

public sealed record CheckAnswerCommand(
    Guid SessionId,
    Guid ItemId,
    Guid UserId,
    bool IsAdmin,
    IReadOnlyList<Guid>? OptionIds,
    string? Text) : ICommand;

public sealed class CheckAnswerCommandValidator : AbstractValidator<CheckAnswerCommand>
{
    public const int MAX_OPTION_IDS = 10;
    public const int MAX_TEXT_LENGTH = 4000;

    public CheckAnswerCommandValidator()
    {
        RuleFor(x => x.OptionIds)
            .Must(ids => ids is null || ids.Count <= MAX_OPTION_IDS)
            .WithError(GeneralErrors.OutOfRange(nameof(CheckAnswerCommand.OptionIds), 0, MAX_OPTION_IDS));

        RuleFor(x => x.Text)
            .Must(text => text is null || text.Length <= MAX_TEXT_LENGTH)
            .WithError(GeneralErrors.LengthIsInvalid(nameof(CheckAnswerCommand.Text), max: MAX_TEXT_LENGTH));
    }
}

public sealed class CheckAnswerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/sessions/{sessionId:guid}/answers/{itemId:guid}/check",
                async Task<EndpointResult<CheckAnswerResponse>> (
                    Guid sessionId,
                    Guid itemId,
                    CheckAnswerRequest request,
                    CheckAnswerHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CheckAnswerCommand(sessionId, itemId, user.UserId, user.IsAdmin, request.OptionIds, request.Text),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Мгновенная проверка ОДНОГО ответа в DRILL/LEARN/тест-сессии. Грейдит детерминированно по
///     снапшотнутому ключу грейдинга (<see cref="AnswerGrader"/>), записывает результат на
///     item, обновляет <see cref="TopicMastery"/> (derived — взвешенное среднее последних баллов по
///     уникальным вопросам; кроме OPEN_TEXT/PENDING — самопроверка,
///     mastery не трогается). Повторная проверка уже отвеченного item'а → 409.
///     <para>
///         <b>Reveal-gate (#568 Ф2):</b> для <c>PER_QUESTION</c>-сессии (DRILL/LEARN — formative)
///         возвращает вердикт/балл + правильный ответ/эталон/разбор сразу. Для
///         <c>END_OF_SESSION</c>-теста (grade-at-end) ответ записывается и грейдится, но в ответе
///         НЕ раскрывается ключ — только вердикт «принято» (PENDING), балл/правильный ответ
///         придут после <c>Complete</c>. Mastery считается в обоих случаях (по реальному баллу).
///     </para>
///     <para>
///         <b>Study-state (LEARN/DRILL):</b> formative-режимы LEARN («тренировка») и DRILL
///         («тест»/«набор тестов») дополнительно апсертят <c>QuestionStudyState</c> вопроса
///         (<c>RecordTestResult</c> → статус KNOWN/WRONG + SRS), так ответы питают «изучено» темы /
///         список / ошибки / повтор. MOCK (экзамен) study-state не трогает. Балл на пользователе не пишется.
///     </para>
///     <para>
///         <b>OPEN_TEXT инлайн-грейдинг (не-мок, #568 W2):</b> в не-MOCK сессии открытый ответ
///         грейдится AI инлайн (<see cref="IOpenAnswerGrader"/>) — балл + вердикт + краткий фидбэк —
///         и дальше обрабатывается как обычный оценённый ответ (mastery + study-state). При сбое/
///         таймауте AI ответ остаётся <c>PENDING</c> без балла и без обновления mastery/study-state
///         (можно перепроверить), endpoint всё равно 200. В MOCK открытые ответы НЕ грейдятся инлайн —
///         их оценивает фоновый <c>MockAnswerGradingService</c> после Complete.
///     </para>
/// </summary>
public sealed class CheckAnswerHandler : ICommandHandler<CheckAnswerResponse, CheckAnswerCommand>
{
    private readonly IValidator<CheckAnswerCommand> _validator;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly SessionAnswerGrading _grading;
    private readonly IEntitlementChecker _entitlements;
    private readonly TrainerQuotaService _quota;
    private readonly TrainerOpenGradeRateLimiter _openGradeRateLimiter;
    private readonly AiUsageLedger _aiUsageLedger;
    private readonly ITransactionManager _transactions;

    public CheckAnswerHandler(
        IValidator<CheckAnswerCommand> validator,
        ITrainingSessionsRepository sessions,
        SessionAnswerGrading grading,
        IEntitlementChecker entitlements,
        TrainerQuotaService quota,
        TrainerOpenGradeRateLimiter openGradeRateLimiter,
        AiUsageLedger aiUsageLedger,
        ITransactionManager transactions)
    {
        _validator = validator;
        _sessions = sessions;
        _grading = grading;
        _entitlements = entitlements;
        _quota = quota;
        _openGradeRateLimiter = openGradeRateLimiter;
        _aiUsageLedger = aiUsageLedger;
        _transactions = transactions;
    }

    public async Task<Result<CheckAnswerResponse, Error>> Handle(
        CheckAnswerCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == command.SessionId && s.UserId == command.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(command.SessionId);

        TrainingSession session = sessionResult.Value;

        TrainingSessionItem? item = session.Items.FirstOrDefault(i => i.Id == command.ItemId);
        if (item is null)
            return TrainerServiceErrors.Session.ItemNotFound(command.ItemId);

        // Идемпотентность: уже отвеченный item не перепроверяем — 409.
        if (item.AnsweredAt is not null)
            return TrainerServiceErrors.Session.AnswerAlreadyChecked();

        // Auto-gradable types (choice / EXACT_TEXT) → deterministic. OPEN_TEXT in a non-MOCK session →
        // inline AI grade (with a fail-soft fallback to PENDING). OPEN_TEXT in a MOCK session stays
        // PENDING here — the background grader scores it after Complete. Запись/mastery/study-state +
        // reveal-gate целиком живут в общем SessionAnswerGrading (тот же путь, что и у голосового ответа).
        bool isOpenText = string.Equals(item.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal);
        bool gradeOpenInline = isOpenText && session.Mode != TrainingMode.MOCK;

        CheckAnswerResponse response;
        // AI usage of the inline open-answer grade (#614 C1) — recorded in the ledger after the answer
        // is persisted. null on auto-graded / fallback / empty-answer paths (no billable LLM call).
        AiUsage? gradeUsage = null;
        string gradeModel = string.Empty;

        if (gradeOpenInline)
        {
            // Per-user OPEN_GRADE gate (#614 C2): only this path makes a billable LLM call. Resolve PRO,
            // rate-limit, and consume one quota unit BEFORE grading. The PRO state is resolved here (only
            // when an inline grade is about to run) to avoid an extra Redis round-trip on the auto path.
            bool hasPro = await TrainerProAccessPolicy.HasProAsync(
                command.UserId, command.IsAdmin, _entitlements, cancellationToken);

            // Монетизация по типу вопроса (#623): развёрнутый (OPEN_TEXT) ответ с AI-анализом — только
            // PRO. Free-юзер получает чистый 403 trainer.pro.required (фронт уже показал замок «Доступно
            // на полном доступе» на этом item'е через SessionItemDto.IsLocked) вместо непонятного
            // quota.exceeded. Admin → hasPro=true, проходит.
            if (!hasPro)
                return TrainerServiceErrors.Access.ProRequired();

            UnitResult<Error> rateLimitResult = await _openGradeRateLimiter.TryAcquireAsync(
                command.UserId, command.IsAdmin, cancellationToken);
            if (rateLimitResult.IsFailure)
                return rateLimitResult.Error;

            // Per-user OPEN_GRADE quota (#614 C2) — анти-абуз потолок PRO (день), не free-гейт.
            UnitResult<Error> quotaResult = await _quota.TryConsumeAsync(
                command.UserId, QuotaDimension.OPEN_GRADE, hasPro, cancellationToken);
            if (quotaResult.IsFailure)
                return quotaResult.Error;

            Result<InlineOpenGradeResult, Error> graded = await _grading.GradeAndRecordOpenInlineAsync(
                session, item, command.UserId, command.Text, cancellationToken);
            if (graded.IsFailure)
                return graded.Error;

            response = graded.Value.Response;
            gradeUsage = graded.Value.Usage;
            gradeModel = graded.Value.Model;
        }
        else
        {
            Result<CheckAnswerResponse, Error> recorded = await _grading.RecordAutoGradedAsync(
                session, item, command.UserId, command.OptionIds, command.Text, cancellationToken);
            if (recorded.IsFailure)
                return recorded.Error;

            response = recorded.Value;
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // AI-usage ledger (#614 C1): record the inline open-answer LLM grade, if one actually ran.
        // A real LLM call sets the model (the empty-answer / fallback paths leave it empty → no call →
        // nothing to bill). Best-effort: a ledger error never fails this already-saved answer.
        if (!string.IsNullOrEmpty(gradeModel))
        {
            await _aiUsageLedger.RecordLlmAsync(
                command.UserId,
                AiUsageOperation.OPEN_ANSWER_GRADE,
                gradeModel,
                gradeUsage,
                session.Id,
                cancellationToken);
        }

        return response;
    }
}
