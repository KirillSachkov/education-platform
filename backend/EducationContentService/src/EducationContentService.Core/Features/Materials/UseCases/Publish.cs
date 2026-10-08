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

public sealed record PublishMaterialCommand(Guid MaterialId, bool NotifySubscribers) : ICommand;

public sealed record PublishMaterialRequest(bool NotifySubscribers = true);

public sealed class PublishMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("materials/{materialId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid materialId,
                    [FromBody] PublishMaterialRequest? request,
                    [FromServices] PublishMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new PublishMaterialCommand(materialId, request?.NotifySubscribers ?? true),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class PublishMaterialHandler : ICommandHandler<Guid, PublishMaterialCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<PublishMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public PublishMaterialHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<PublishMaterialHandler> logger,
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
        PublishMaterialCommand command,
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

        UnitResult<Error> publishResult = material.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(material.Id, cancellationToken);

        await _outbox.PublishAsync(new MaterialPublished(
            MaterialId: material.Id,
            Title: material.Title.Value,
            AuthorId: material.AuthorId,
            CourseIds: courseIds,
            NotifySubscribers: command.NotifySubscribers));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Опубликованный материал должен немедленно появиться в curriculum/landing
        // во всех курсах, где он привязан. Иначе автор видит «материал
        // опубликован» в toast'е, но в сайдбаре он не появляется до 3 минут.
        // Доменная операция уже зафиксирована — недоступность Redis не должна
        // превращаться в 500. В худшем случае кэш истечёт по TTL.
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
                "Curriculum cache invalidation failed after publishing material {MaterialId} (will expire via TTL)",
                material.Id);
        }

        _logger.LogInformation("Material {MaterialId} published", material.Id);

        return material.Id;
    }
}
