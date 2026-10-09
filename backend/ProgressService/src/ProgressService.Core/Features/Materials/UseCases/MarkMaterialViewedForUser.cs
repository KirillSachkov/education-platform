using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Materials;

namespace ProgressService.Core.Features.Materials.UseCases;

/// <summary>
///     Staff-override: отметить материал изученным ЗА студента (issue #398). Зеркало self-эндпоинта
///     <see cref="MarkMaterialViewedHandler"/>, но target — указанный <c>UserId</c> из тела, а не
///     <c>_user.UserId</c>. Поднимает <see cref="Domain.Materials.Events.MaterialViewedEvent"/> с
///     target-юзером → каскад <c>module_item_progress</c> начисляются ЦЕЛЕВОМУ юзеру.
///     Идемпотентно по паре (target UserId, MaterialId).
///     <para>
///         Авторизация (как у <c>ApproveIssue</c>): Tier-1 — <c>Progress.MANAGE</c> (admin/moderator),
///         плюс in-handler author-owner проверка (автор материала). Tier-3 — entitlement-check против
///         ЦЕЛЕВОГО юзера (не вызывающего): если у target нет доступа к материалу — 403, строка не
///         создаётся (иначе получили бы phantom progress на закрытом контенте).
///     </para>
/// </summary>
public sealed record MarkMaterialViewedForUserCommand(Guid MaterialId, Guid UserId) : ICommand;

public sealed class MarkMaterialViewedForUserCommandValidator : AbstractValidator<MarkMaterialViewedForUserCommand>
{
    public MarkMaterialViewedForUserCommandValidator()
    {
        RuleFor(x => x.MaterialId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkMaterialViewedForUserCommand.MaterialId)));

        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkMaterialViewedForUserCommand.UserId)));
    }
}

public sealed class MarkMaterialViewedForUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/materials/{materialId:guid}/view-for-user",
                async Task<EndpointResult> (
                        Guid materialId,
                        MarkMaterialViewedForUserRequest request,
                        MarkMaterialViewedForUserHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new MarkMaterialViewedForUserCommand(materialId, request.UserId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class MarkMaterialViewedForUserHandler : ICommandHandler<MarkMaterialViewedForUserCommand>
{
    private const string MATERIAL_ENTITY_TYPE = "material";

    private readonly IMaterialViewRepository _materialViewRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MarkMaterialViewedForUserCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<MarkMaterialViewedForUserHandler> _logger;

    public MarkMaterialViewedForUserHandler(
        IMaterialViewRepository materialViewRepository,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        IValidator<MarkMaterialViewedForUserCommand> validator,
        UserScopedData user,
        ILogger<MarkMaterialViewedForUserHandler> logger)
    {
        _materialViewRepository = materialViewRepository;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(
        MarkMaterialViewedForUserCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Tier-1 уже отсекла всех без Progress.MANAGE (admin/moderator). Дополнительно — author-owner
        // ветка по образцу ApproveIssue: автор материала может отмечать своим студентам. Если материал
        // не привязан к курсу (AuthorId == null) — только admin/moderator проходят.
        UnitResult<Error> authorizationResult = await EnsureCanMarkForOthersAsync(command.MaterialId, cancellationToken);
        if (authorizationResult.IsFailure)
        {
            return authorizationResult.Error;
        }

        // Tier-3 — entitlement против ЦЕЛЕВОГО юзера (не вызывающего): subject не-admin, поэтому
        // bypass'а нет — проверяются реальные plan-теги target'а в Redis. Без доступа — 403 и строку
        // не создаём, иначе получим phantom progress на закрытом контенте.
        var targetSubject = new AccessSubject(
            IsAuthenticated: true,
            UserId: command.UserId,
            IsAdmin: false);

        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            targetSubject,
            ResourceTypes.MATERIAL,
            command.MaterialId,
            cancellationToken);

        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.MaterialAccessDenied();
        }

        // Те же три кейса, что и в self-версии, но target = command.UserId. Нет записи —
        // создаём completed (event внутри factory). Есть silent track — MarkAsCompleted поднимает
        // event. Уже completed — no-op (идемпотентность повторного override).
        Guid targetUserId = command.UserId;
        Guid materialId = command.MaterialId;
        Result<MaterialView, Error> existingResult = await _materialViewRepository
            .GetByAsync(
                v => v.UserId == targetUserId && v.MaterialId == materialId,
                cancellationToken);

        if (existingResult.IsFailureExceptNotFound())
        {
            return existingResult.Error;
        }

        if (existingResult.IsSuccess)
        {
            bool stateChanged = existingResult.Value.MarkAsCompleted();
            if (!stateChanged)
            {
                return UnitResult.Success<Error>();
            }
        }
        else
        {
            Result<MaterialView, Error> createResult = MaterialView.CreateCompleted(targetUserId, materialId);
            if (createResult.IsFailure)
            {
                return createResult.Error;
            }

            await _materialViewRepository.AddAsync(createResult.Value, cancellationToken);
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Material marked as completed on behalf of student. TargetUserId: {TargetUserId}, MaterialId: {MaterialId}, ActorId: {ActorId}",
            targetUserId,
            materialId,
            _user.UserId);

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> EnsureCanMarkForOthersAsync(
        Guid materialId,
        CancellationToken cancellationToken)
    {
        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        if (isPrivileged)
        {
            return UnitResult.Success<Error>();
        }

        // Author-owner: материал должен принадлежать автору-вызывающему. Орфан-материал (без
        // course-привязки) не имеет автора → доступ только privileged-ролям.
        Result<EntityOwnershipDto, Error> ownershipResult = await _ecsClient
            .GetEntityOwnershipAsync(MATERIAL_ENTITY_TYPE, materialId, cancellationToken);
        if (ownershipResult.IsFailure)
        {
            return ProgressErrors.MaterialAccessDenied();
        }

        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR)
            && ownershipResult.Value.AuthorId is Guid authorId
            && authorId == _user.UserId;

        return isAuthorOwner
            ? UnitResult.Success<Error>()
            : ProgressErrors.MaterialAccessDenied();
    }
}