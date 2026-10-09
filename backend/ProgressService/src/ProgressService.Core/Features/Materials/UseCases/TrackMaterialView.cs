using ContentAccess;
using Core.Abstractions;
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

namespace ProgressService.Core.Features.Materials.UseCases;

/// <summary>
///     Silent track-view: фиксирует факт «пользователь зашёл на страницу материала» —
///     без cascade на <c>module_item_progress</c>, без domain event'ов. Питает
///     ТОЛЬКО публичный счётчик «N просмотров» (#234) для auth-юзеров. Дёргается
///     <c>useTrackMaterialView</c> на mount detail-страницы. Issue #285.
///
///     Идемпотентно по паре (UserId, MaterialId). Если запись уже есть (любого
///     <c>is_completed</c>) — no-op: silent track НЕ должен понижать предыдущий
///     явный mark до «не изучено».
///
///     Для анонимов есть отдельный endpoint <c>POST /anonymous-view</c> →
///     <see cref="RecordAnonymousMaterialViewCommand"/>, который пишет в
///     <c>anonymous_material_views</c>.
/// </summary>
public sealed record TrackMaterialViewCommand(Guid MaterialId) : ICommand;

public sealed class TrackMaterialViewCommandValidator : AbstractValidator<TrackMaterialViewCommand>
{
    public TrackMaterialViewCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(TrackMaterialViewCommand.MaterialId)));
    }
}

public sealed class TrackMaterialViewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/materials/{materialId:guid}/track-view",
                async Task<EndpointResult> (
                        Guid materialId,
                        TrackMaterialViewHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new TrackMaterialViewCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class TrackMaterialViewHandler : ICommandHandler<TrackMaterialViewCommand>
{
    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IValidator<TrackMaterialViewCommand> _validator;
    private readonly UserScopedData _user;

    public TrackMaterialViewHandler(
        IMaterialViewRepository materialViewRepository,
        IEntitlementChecker entitlementChecker,
        IValidator<TrackMaterialViewCommand> validator,
        UserScopedData user)
    {
        _materialViewRepository = materialViewRepository;
        _entitlementChecker = entitlementChecker;
        _validator = validator;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(TrackMaterialViewCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Не считаем «просмотр» для тех, кто даже не имеет права читать материал. PUBLIC/REGISTERED
        // → granted всем подходящим, FREE/ENROLLED → check plan-grant'ов. На UI заблокированный
        // материал и так не вызовет track-view (хук фильтрует по isAccessible), но эту защиту
        // оставляем на случай прямого вызова API.
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.MATERIAL,
            command.MaterialId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.MaterialAccessDenied();
        }

        // ON CONFLICT DO NOTHING — идемпотентность + race-safety без extra SELECT'а.
        // Не публикует domain event'ов и не использует ITransactionManager: чистый INSERT
        // в одну строку через Dapper, outbox flush не нужен.
        await _materialViewRepository.TryInsertTrackAsync(
            _user.UserId,
            command.MaterialId,
            cancellationToken);

        return UnitResult.Success<Error>();
    }
}