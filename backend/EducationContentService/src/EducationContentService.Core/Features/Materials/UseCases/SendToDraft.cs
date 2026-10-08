using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record SendMaterialToDraftCommand(Guid MaterialId) : ICommand;

public sealed class SendMaterialToDraftEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("materials/{materialId:guid}/draft", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid materialId,
                    [FromServices] SendMaterialToDraftHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new SendMaterialToDraftCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class SendMaterialToDraftHandler : ICommandHandler<Guid, SendMaterialToDraftCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<SendMaterialToDraftHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public SendMaterialToDraftHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<SendMaterialToDraftHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        SendMaterialToDraftCommand command,
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

        UnitResult<Error> draftResult = material.SendToDraft();
        if (draftResult.IsFailure)
            return draftResult.Error;

        List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(material.Id, cancellationToken);

        await _outbox.PublishAsync(new MaterialSentToDraft(material.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Material исчез из public curriculum своих курсов — сбрасываем кэш.
        // Недоступность Redis не откатывает уже зафиксированную транзакцию.
        try
        {
            foreach (Guid courseId in courseIds)
            {
                await CourseCacheInvalidator.InvalidateAsync(_cache, courseId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Curriculum cache invalidation failed after sending material {MaterialId} to draft (will expire via TTL)",
                material.Id);
        }

        _logger.LogInformation("Material {MaterialId} reverted to draft", material.Id);

        return material.Id;
    }
}
