using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Collections;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record UpdateSectionCommand(
    Guid CollectionId,
    Guid SectionId,
    UpdateSectionRequest Request) : ICommand;

public sealed class UpdateSectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("collections/{collectionId:guid}/sections/{sectionId:guid}",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromRoute] Guid sectionId,
                    [FromBody] UpdateSectionRequest request,
                    [FromServices] UpdateSectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new UpdateSectionCommand(collectionId, sectionId, request),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class UpdateSectionHandler : ICommandHandler<Guid, UpdateSectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<UpdateSectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateSectionHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ITransactionManager transactionManager,
        ILogger<UpdateSectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateSectionCommand command,
        CancellationToken cancellationToken)
    {
        Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
            c => c.Id == command.CollectionId, cancellationToken);
        if (collectionResult.IsFailure)
            return collectionResult.Error;

        Collection collection = collectionResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(collection.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<CollectionSection, Error> sectionResult = await _sectionsRepository.GetByAsync(
            s => s.Id == command.SectionId && s.CollectionId == collection.Id,
            cancellationToken: cancellationToken);
        if (sectionResult.IsFailure)
            return EducationErrors.ItemNotFound("CollectionSection", command.SectionId);

        sectionResult.Value.Update(command.Request.Title, command.Request.Description);
        collection.Touch();

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Section {SectionId} updated in collection {CollectionId}",
            command.SectionId, collection.Id);

        return command.SectionId;
    }
}
