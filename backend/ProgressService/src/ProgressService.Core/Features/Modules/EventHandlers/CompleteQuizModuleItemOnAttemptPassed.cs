using ContentAccess;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using PlatformAuth;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Quizzes.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Modules.EventHandlers;

/// <summary>
///     Cross-enrollment cascade (ST-13 #493, зеркало <see cref="CompleteModuleItemOnMaterialViewed"/>):
///     passed-попытка квиза закрывает пункт модуля во ВСЕХ курсах, где квиз размещён
///     (module_items item_type='Quiz') и к которым у пользователя есть доступ. Решение
///     владельца: завершение элемента = ПРОХОДНОЙ БАЛЛ, не любая попытка.
/// </summary>
/// <remarks>
///     Flow:
///     <list type="number">
///         <item>Из ECS получаем (CourseId, ModuleId, ModuleItemsTotal) для квиза
///             (<c>GET /internal/quizzes/{id}/module-lookup</c>).</item>
///         <item>Для каждого course-context проверяем entitlement (Redis SINTER по курсу).
///             Без доступа — пропускаем (ленивый anchor не создаём).</item>
///         <item>Для entitled-курсов ensure-create'им progress-anchor, затем
///             ensure module_progress + complete module_item_progress (item_type=QUIZ).</item>
///     </list>
///     Handler идемпотентен: событие поднимается на каждой passed-попытке, но
///     <c>CompleteQuizModuleItemAsync</c> no-op'ит уже закрытые пункты. Запускается в той же
///     транзакции, что и <c>SubmitQuizAttempt</c> — добавленный anchor сохранится её
///     <c>SaveChangesAsync</c>. Без XP в этой итерации.
/// </remarks>
public sealed class CompleteQuizModuleItemOnAttemptPassed : IDomainEventHandler<QuizAttemptPassedEvent>
{
    private readonly IModuleProgressService _moduleProgressService;
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly UserScopedData _user;
    private readonly ILogger<CompleteQuizModuleItemOnAttemptPassed> _logger;

    public CompleteQuizModuleItemOnAttemptPassed(
        IModuleProgressService moduleProgressService,
        IEnrollmentAnchorService enrollmentAnchorService,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        UserScopedData user,
        ILogger<CompleteQuizModuleItemOnAttemptPassed> logger)
    {
        _moduleProgressService = moduleProgressService;
        _enrollmentAnchorService = enrollmentAnchorService;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(QuizAttemptPassedEvent domainEvent, CancellationToken ct)
    {
        Result<IReadOnlyList<QuizModuleContextDto>, Error> contextsResult = await _ecsClient
            .GetQuizModuleLookupAsync(domainEvent.QuizId, ct);
        if (contextsResult.IsFailure)
        {
            // ECS недоступен — попытка уже сохранена (passed-факт не теряется), но
            // module_item_progress не обновится в этот раз. Ре-синк — следующая
            // passed-попытка (событие поднимается на каждой).
            _logger.LogWarning(
                "Failed to resolve module contexts for quiz {QuizId}: {Error}",
                domainEvent.QuizId,
                contextsResult.Error);
            return UnitResult.Success<Error>();
        }

        IReadOnlyList<QuizModuleContextDto> contexts = contextsResult.Value;
        if (contexts.Count == 0)
        {
            // Квиз не размещён ни в одном модуле (standalone / только в course_quizzes) —
            // cascade не применим.
            return UnitResult.Success<Error>();
        }

        Guid eventUserId = domainEvent.UserId;

        // Subject для per-course entitlement-чека. IsAdmin — из request-scoped UserScopedData
        // (handler выполняется синхронно внутри HTTP-запроса SubmitQuizAttempt).
        var subject = new AccessSubject(
            IsAuthenticated: true,
            UserId: eventUserId,
            IsAdmin: _user.IsAdmin);

        foreach (QuizModuleContextDto context in contexts)
        {
            // Per-course entitlement: cascade'им и создаём anchor только для курсов, к которым
            // у пользователя реально есть доступ. Квиз может быть PUBLIC (попытка прошла без
            // grant'а), но прогресс на чужом платном курсе открываться не должен.
            AccessDecision courseAccess = await _entitlementChecker.CheckAccessAsync(
                subject, ResourceTypes.COURSE, context.CourseId, ct);
            if (!courseAccess.IsGranted)
            {
                continue;
            }

            Result<CourseDto, Error> courseLookup = await _ecsClient
                .GetCourseLookupAsync(context.CourseId, ct);
            if (courseLookup.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to resolve author for course {CourseId} during quiz-passed cascade: {Error}",
                    context.CourseId,
                    courseLookup.Error);
                continue;
            }

            Result<CourseEnrollment, Error> anchorResult = await _enrollmentAnchorService
                .EnsureEnrollmentAsync(
                    eventUserId,
                    context.CourseId,
                    courseLookup.Value.AuthorId,
                    EnrollmentSource.ENGAGEMENT,
                    ct);
            if (anchorResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to ensure enrollment anchor for user {UserId} on course {CourseId}: {Error}",
                    eventUserId,
                    context.CourseId,
                    anchorResult.Error);
                continue;
            }

            Guid enrollmentId = anchorResult.Value.Id;

            UnitResult<Error> ensureModuleResult = await _moduleProgressService.EnsureModuleProgressAsync(
                enrollmentId,
                context.ModuleId,
                context.ModuleItemsTotal,
                ct);
            if (ensureModuleResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to ensure ModuleProgress for enrollment {EnrollmentId}, module {ModuleId}: {Error}",
                    enrollmentId,
                    context.ModuleId,
                    ensureModuleResult.Error);
                continue;
            }

            UnitResult<Error> completeResult = await _moduleProgressService.CompleteQuizModuleItemAsync(
                enrollmentId,
                context.ModuleId,
                domainEvent.QuizId,
                ct);
            if (completeResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to complete quiz ModuleItemProgress for enrollment {EnrollmentId}, quiz {QuizId}: {Error}",
                    enrollmentId,
                    domainEvent.QuizId,
                    completeResult.Error);
                // не прерываем cascade для остальных enrollments
            }
        }

        return UnitResult.Success<Error>();
    }
}
