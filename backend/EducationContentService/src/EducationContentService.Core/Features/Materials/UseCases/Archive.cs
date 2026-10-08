using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record ArchiveMaterialCommand(Guid MaterialId) : ICommand;

public sealed class ArchiveMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("materials/{materialId:guid}/archive", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid materialId,
                    [FromServices] ArchiveMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ArchiveMaterialCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class ArchiveMaterialHandler : ICommandHandler<Guid, ArchiveMaterialCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<ArchiveMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public ArchiveMaterialHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<ArchiveMaterialHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        ArchiveMaterialCommand command,
        CancellationToken cancellationToken)
    {
        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId,
            cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        Material material = materialResult.Value;

        UnitResult<Error> ownership = await _userScopedData.CheckMaterialOwnershipAsync(
            material.Id, material.AuthorId, _materialsRepository, cancellationToken);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> archiveResult = material.Archive();
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        await _outbox.PublishAsync(new MaterialArchived(material.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Material {MaterialId} archived", material.Id);

        return material.Id;
    }
}
