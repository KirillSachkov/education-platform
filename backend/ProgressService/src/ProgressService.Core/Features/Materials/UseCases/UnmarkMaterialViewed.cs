using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Materials;

namespace ProgressService.Core.Features.Materials.UseCases;

/// <summary>
///     Снимает явную отметку «материал изучен» — обратная операция к
///     <see cref="MarkMaterialViewedCommand"/>. Запись в <c>material_views</c> НЕ удаляется
///     (issue #285): мы только переключаем <c>is_completed: true → false</c>, чтобы
///     сохранить факт визита для счётчика «N просмотров» (#234). Идемпотентно: если записи
///     нет ИЛИ она уже не completed — возвращает успех, ничего не меняет. Каскадно откатывает
///     <c>module_item_progress</c> во всех активных enrollment'ах пользователя, где материал
///     есть (через ECS course-context lookup), декрементируя <c>module_progress.items_completed</c>
///     и возвращая модуль из COMPLETED в IN_PROGRESS при необходимости.
/// </summary>
public sealed record UnmarkMaterialViewedCommand(Guid MaterialId) : ICommand;

public sealed class UnmarkMaterialViewedCommandValidator : AbstractValidator<UnmarkMaterialViewedCommand>
{
    public UnmarkMaterialViewedCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UnmarkMaterialViewedCommand.MaterialId)));
    }
}

public sealed class UnmarkMaterialViewedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/progress/materials/{materialId:guid}/view",
                async Task<EndpointResult> (
                        Guid materialId,
                        UnmarkMaterialViewedHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new UnmarkMaterialViewedCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class UnmarkMaterialViewedHandler : ICommandHandler<UnmarkMaterialViewedCommand>
{
    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly IModuleProgressService _moduleProgressService;
    private readonly ICourseEnrollmentRepository _courseEnrollmentRepository;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UnmarkMaterialViewedCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<UnmarkMaterialViewedHandler> _logger;

    public UnmarkMaterialViewedHandler(
        IMaterialViewRepository materialViewRepository,
        IModuleProgressService moduleProgressService,
        ICourseEnrollmentRepository courseEnrollmentRepository,
        IEducationContentServiceClient ecsClient,
        IEntitlementChecker entitlementChecker,
        ITransactionManager transactionManager,
        IValidator<UnmarkMaterialViewedCommand> validator,
        UserScopedData user,
        ILogger<UnmarkMaterialViewedHandler> logger)
    {
        _materialViewRepository = materialViewRepository;
        _moduleProgressService = moduleProgressService;
        _courseEnrollmentRepository = courseEnrollmentRepository;
        _ecsClient = ecsClient;
        _entitlementChecker = entitlementChecker;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(UnmarkMaterialViewedCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Те же entitlement-правила, что и у POST: пользователь должен иметь доступ
        // к материалу (PUBLIC/REGISTERED/FREE-plan/ENROLLED). Без этого unmark — bypass
        // обычной авторизации. Admin / ContentManager — bypass через сам checker.
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.MATERIAL,
            command.MaterialId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.MaterialAccessDenied();
        }

        Guid userId = _user.UserId;
        Guid materialId = command.MaterialId;

        Result<MaterialView, Error> existingResult = await _materialViewRepository
            .GetByAsync(
                v => v.UserId == userId && v.MaterialId == materialId,
                cancellationToken);

        if (existingResult.IsNotFound())
        {
            // Идемпотентность: ничего не отмечено — нечего откатывать.
            return UnitResult.Success<Error>();
        }

        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        // Если запись существует, но silent track (is_completed=false) — никакого cascade
        // и SaveChanges не нужны (отметки «изучено» и не было).
        bool stateChanged = existingResult.Value.UnmarkAsCompleted();
        if (!stateChanged)
        {
            return UnitResult.Success<Error>();
        }

        // Cascade-revert на module_item_progress во всех активных enrollment'ах юзера,
        // где материал входит в курс. Зеркало <c>CompleteModuleItemOnMaterialViewed</c>:
        // тот резолвит course-contexts из ECS и завершает items, мы — откатываем.
        Result<IReadOnlyList<MaterialCourseContextDto>, Error> contextsResult = await _ecsClient
            .GetMaterialCourseContextsAsync(materialId, cancellationToken);
        if (contextsResult.IsFailure)
        {
            // ECS недоступен — без course-contexts мы не знаем, какие module_item_progress
            // откатывать. Прерываемся: лучше оставить рассинхрон видимым (просмотр всё ещё
            // отмечен), чем удалить факт просмотра без cascade и оставить «галки» в модулях.
            _logger.LogWarning(
                "Failed to resolve course contexts for material {MaterialId} during unmark: {Error}",
                materialId,
                contextsResult.Error);
            return contextsResult.Error;
        }

        IReadOnlyList<MaterialCourseContextDto> contexts = contextsResult.Value;

        if (contexts.Count > 0)
        {
            IReadOnlyList<CourseEnrollment> userEnrollments = await _courseEnrollmentRepository
                .GetManyByAsync(
                    e => e.UserId == userId,
                    cancellationToken);

            Dictionary<Guid, Guid> enrollmentByCourse = userEnrollments
                .ToDictionary(e => e.CourseId, e => e.Id);

            foreach (MaterialCourseContextDto context in contexts)
            {
                if (!enrollmentByCourse.TryGetValue(context.CourseId, out Guid enrollmentId))
                {
                    continue;
                }

                UnitResult<Error> uncompleteResult = await _moduleProgressService
                    .UncompleteMaterialModuleItemAsync(enrollmentId, materialId, cancellationToken);

                if (uncompleteResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to uncomplete ModuleItemProgress for enrollment {EnrollmentId}, material {MaterialId}: {Error}",
                        enrollmentId,
                        materialId,
                        uncompleteResult.Error);
                    // не прерываем cascade для остальных enrollments
                }
            }
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Material completion unmarked. UserId: {UserId}, MaterialId: {MaterialId}",
            userId,
            materialId);

        return UnitResult.Success<Error>();
    }
}