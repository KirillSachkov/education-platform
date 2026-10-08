using ContentAccess;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using PlatformAuth;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Materials.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Modules.EventHandlers;

/// <summary>
///     Cross-enrollment cascade: факт просмотра материала пользователем закрывает пункт
///     модуля во ВСЕХ курсах, где материал размещён и к которым у пользователя есть доступ.
///     Один просмотр двигает прогресс во всех таких курсах.
/// </summary>
/// <remarks>
///     Flow (access-derive-model Phase 2 — ленивые progress-anchors):
///     <list type="number">
///         <item>Из ECS получаем (CourseId, ModuleId, ModuleItemsTotal) для материала.</item>
///         <item>Для каждого course-context проверяем entitlement (Redis SINTER по курсу).
///             Без доступа — пропускаем (ленивый anchor не создаём).</item>
///         <item>Для entitled-курсов <b>ensure-create'им</b> progress-anchor (раньше cascade
///             молча пропускал курсы без enrollment'а → прогресс терялся), затем
///             ensure module_progress + complete module_item_progress.</item>
///     </list>
///     Handler идемпотентен (повторный event не придёт благодаря guard'у в
///     <c>MarkMaterialViewedHandler</c>; <c>EnsureEnrollmentAsync</c> и отдельные
///     <c>module_item_progress</c> записи — idempotent). Запускается в той же транзакции, что и
///     <c>MarkMaterialViewed</c>, поэтому добавленный anchor сохранится её <c>SaveChangesAsync</c>.
/// </remarks>
public sealed class CompleteModuleItemOnMaterialViewed : IDomainEventHandler<MaterialViewedEvent>
{
    private readonly IModuleProgressService _moduleProgressService;
    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly UserScopedData _user;
    private readonly ILogger<CompleteModuleItemOnMaterialViewed> _logger;

    public CompleteModuleItemOnMaterialViewed(
        IModuleProgressService moduleProgressService,
        IEnrollmentAnchorService enrollmentAnchorService,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        UserScopedData user,
        ILogger<CompleteModuleItemOnMaterialViewed> logger)
    {
        _moduleProgressService = moduleProgressService;
        _enrollmentAnchorService = enrollmentAnchorService;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(MaterialViewedEvent domainEvent, CancellationToken ct)
    {
        Result<IReadOnlyList<MaterialCourseContextDto>, Error> contextsResult = await _ecsClient
            .GetMaterialCourseContextsAsync(domainEvent.MaterialId, ct);
        if (contextsResult.IsFailure)
        {
            // ECS недоступен — материал считается просмотренным (MaterialView уже создан),
            // но module_item_progress не обновится в этот раз. Ре-синк — через
            // resync-CLI или следующий просмотр/cascade.
            _logger.LogWarning(
                "Failed to resolve course contexts for material {MaterialId}: {Error}",
                domainEvent.MaterialId,
                contextsResult.Error);
            return UnitResult.Success<Error>();
        }

        IReadOnlyList<MaterialCourseContextDto> contexts = contextsResult.Value;
        if (contexts.Count == 0)
        {
            // Orphan-материал (не в course_materials ни одного курса) — cascade не применим.
            return UnitResult.Success<Error>();
        }

        Guid eventUserId = domainEvent.UserId;

        // Subject для per-course entitlement-чека. Берём IsAdmin из request-scoped UserScopedData
        // (handler выполняется синхронно внутри HTTP-запроса MarkMaterialViewed).
        var subject = new AccessSubject(
            IsAuthenticated: true,
            UserId: eventUserId,
            IsAdmin: _user.IsAdmin);

        foreach (MaterialCourseContextDto context in contexts)
        {
            // Per-course entitlement: cascade'им и создаём anchor только для курсов, к которым
            // у пользователя реально есть доступ (grant'ы). Без этого ленивый anchor открыл бы
            // прогресс на чужих курсах, где материал просто пере-используется.
            AccessDecision courseAccess = await _entitlementChecker.CheckAccessAsync(
                subject, ResourceTypes.COURSE, context.CourseId, ct);
            if (!courseAccess.IsGranted)
            {
                continue;
            }

            // authorId нужен для anchor'а (денорм для лидерборда). Резолвим из ECS.
            // EnsureEnrollmentAsync идемпотентен — при существующем enrollment'е ECS-lookup
            // всё равно дешёвый (кешируется на стороне клиента), а anchor не создаётся повторно.
            Result<CourseDto, Error> courseLookup = await _ecsClient
                .GetCourseLookupAsync(context.CourseId, ct);
            if (courseLookup.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to resolve author for course {CourseId} during material-view cascade: {Error}",
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

            UnitResult<Error> completeResult = await _moduleProgressService.CompleteMaterialModuleItemAsync(
                enrollmentId,
                context.ModuleId,
                domainEvent.MaterialId,
                ct);
            if (completeResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to complete ModuleItemProgress for enrollment {EnrollmentId}, material {MaterialId}: {Error}",
                    enrollmentId,
                    domainEvent.MaterialId,
                    completeResult.Error);
                // не прерываем cascade для остальных enrollments
            }
        }

        return UnitResult.Success<Error>();
    }
}
