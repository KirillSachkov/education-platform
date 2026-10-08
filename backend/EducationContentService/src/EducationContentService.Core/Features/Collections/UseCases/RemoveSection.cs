using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record RemoveSectionCommand(Guid CollectionId, Guid SectionId) : ICommand;

public sealed class RemoveSectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("collections/{collectionId:guid}/sections/{sectionId:guid}",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromRoute] Guid sectionId,
                    [FromServices] RemoveSectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new RemoveSectionCommand(collectionId, sectionId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class RemoveSectionHandler : ICommandHandler<Guid, RemoveSectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<RemoveSectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RemoveSectionHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ITransactionManager transactionManager,
        ILogger<RemoveSectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        RemoveSectionCommand command,
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

        _sectionsRepository.Delete(sectionResult.Value);
        collection.Touch();

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Section {SectionId} removed from collection {CollectionId}",
            command.SectionId, collection.Id);

        return command.SectionId;
    }
}
