using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Collections;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record AddSectionCommand(Guid CollectionId, AddSectionRequest Request) : ICommand;

public sealed class AddSectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("collections/{collectionId:guid}/sections", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromBody] AddSectionRequest request,
                    [FromServices] AddSectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new AddSectionCommand(collectionId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class AddSectionHandler : ICommandHandler<Guid, AddSectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<AddSectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public AddSectionHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ITransactionManager transactionManager,
        ILogger<AddSectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        AddSectionCommand command,
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

        Result<CollectionSection, Error> lastResult = await _sectionsRepository.GetByAsync(
            s => s.CollectionId == collection.Id,
            orderBy: s => s.SortKey,
            descending: true,
            cancellationToken);

        SortKey sortKey = lastResult.IsSuccess
            ? SortKey.After(lastResult.Value.SortKey)
            : SortKey.Initial();

        var section = new CollectionSection(
            collection.Id,
            command.Request.Title,
            command.Request.Description,
            sortKey);

        await _sectionsRepository.AddAsync(section, cancellationToken);
        collection.Touch();

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Section {SectionId} added to collection {CollectionId}",
            section.Id, collection.Id);

        return section.Id;
    }
}
