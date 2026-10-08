using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Modules;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record RestoreModuleCommand(Guid ModuleId) : ICommand;

public sealed class RestoreModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("modules/{moduleId:guid}/restore", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid moduleId,
                    [FromServices] RestoreModuleHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new RestoreModuleCommand(moduleId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class RestoreModuleHandler : ICommandHandler<Guid, RestoreModuleCommand>
{
    private readonly IModulesRepository _modulesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<RestoreModuleHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RestoreModuleHandler(
        IModulesRepository modulesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<RestoreModuleHandler> logger,
        UserScopedData userScopedData)
    {
        _modulesRepository = modulesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        RestoreModuleCommand command, CancellationToken cancellationToken)
    {
        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        Module module = moduleResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(module.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> restoreResult = module.Restore();
        if (restoreResult.IsFailure)
            return restoreResult.Error;

        await _outbox.PublishAsync(new ModuleRestored(module.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Module {ModuleId} restored from archive", module.Id);

        return module.Id;
    }
}
