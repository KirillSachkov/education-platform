using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Materials.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Materials.UseCases;

/// <summary>
///     User-scoped явная отметка «материал изучен» (нажатие кнопки «Отметить изученным»).
///     В отличие от silent track-view'а (<c>POST /track-view</c>, issue #285) — каскадит
///     <c>module_item_progress</c> во всех активных enrollment'ах через
///     <c>CompleteModuleItemOnMaterialViewed</c> и начисляет XP через
///     <c>AwardXpOnMaterialViewed</c>. Идемпотентно: повторный mark не дублирует XP/каскад,
///     потому что событие <see cref="Domain.Materials.Events.MaterialViewedEvent"/>
///     поднимается только на переходе <c>is_completed: false → true</c>.
/// </summary>
public sealed record MarkMaterialViewedCommand(Guid MaterialId) : ICommand;

public sealed class MarkMaterialViewedCommandValidator : AbstractValidator<MarkMaterialViewedCommand>
{
    public MarkMaterialViewedCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkMaterialViewedCommand.MaterialId)));
    }
}

public sealed class MarkMaterialViewedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/materials/{materialId:guid}/view",
                async Task<EndpointResult> (
                        Guid materialId,
                        MarkMaterialViewedHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new MarkMaterialViewedCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class MarkMaterialViewedHandler : ICommandHandler<MarkMaterialViewedCommand>
{
    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MarkMaterialViewedCommand> _validator;
    private readonly UserScopedData _user;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<MarkMaterialViewedHandler> _logger;

    public MarkMaterialViewedHandler(
        IMaterialViewRepository materialViewRepository,
        IEntitlementChecker entitlementChecker,
        ITransactionManager transactionManager,
        IValidator<MarkMaterialViewedCommand> validator,
        UserScopedData user,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<MarkMaterialViewedHandler> logger)
    {
        _materialViewRepository = materialViewRepository;
        _entitlementChecker = entitlementChecker;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _domainEventDispatcher = domainEventDispatcher;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(MarkMaterialViewedCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Entitlement-check на основе Redis-тегов: PUBLIC → grant всем, REGISTERED → auth'нутым,
        // FREE/ENROLLED → проверка course:{id} тегов user'а. Материалы не в курсах (orphan)
        // получают тег sentinel и закрываются для внешних, либо PUBLIC и открыты всем.
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.MATERIAL,
            command.MaterialId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.MaterialAccessDenied();
        }

        // Три кейса в одном атомарном SQL-path:
        // 1) Записи нет → создаём сразу как completed.
        // 2) Silent track уже есть → апгрейдим до completed.
        // 3) Уже completed → no-op.
        // Это закрывает race: useTrackMaterialView стреляет на mount, а пользователь может
        // сразу нажать explicit mark до завершения track-запроса.
        Guid userId = _user.UserId;
        Guid materialId = command.MaterialId;
        UnitResult<Error> beginResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginResult.IsFailure)
        {
            return beginResult.Error;
        }

        Result<MaterialViewCompletionResult, Error> completeResult = await _materialViewRepository
            .CompleteAsync(userId, materialId, cancellationToken);
        if (completeResult.IsFailure)
        {
            return completeResult.Error;
        }

        if (completeResult.Value.StateChanged)
        {
            UnitResult<Error> dispatchResult = await _domainEventDispatcher.DispatchAsync(
                new MaterialViewedEvent(userId, materialId, completeResult.Value.ViewedAt),
                cancellationToken);

            if (dispatchResult.IsFailure)
            {
                return dispatchResult.Error;
            }
        }

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
        {
            return commitResult.Error;
        }

        _logger.LogInformation(
            "Material marked as completed. UserId: {UserId}, MaterialId: {MaterialId}",
            _user.UserId,
            command.MaterialId);

        return UnitResult.Success<Error>();
    }
}
