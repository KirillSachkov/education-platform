using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Collections;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.ValueObjects;
using FileService.Contracts.Dtos;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record UpdateCollectionCommand(Guid CollectionId, UpdateCollectionRequest Request) : ICommand;

public sealed class UpdateCollectionRequestValidator : AbstractValidator<UpdateCollectionRequest>
{
    public UpdateCollectionRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        When(x => x.Description is not null, () =>
        {
            RuleFor(x => x.Description!).MustBeValueObject(Description.Create);
        });
        // AccessType обязателен на Update: пустая строка / null от JSON-байндера идёт сюда,
        // и Must() без NotEmpty() дал бы misleading `content.access_type.invalid` вместо required.
        RuleFor(x => x.AccessType)
            .NotEmpty()
            .WithError(EducationErrors.InvalidAccessType())
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType());
    }
}

public sealed class UpdateCollectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("collections/{collectionId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromBody] UpdateCollectionRequest request,
                    [FromServices] UpdateCollectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateCollectionCommand(collectionId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class UpdateCollectionHandler : ICommandHandler<Guid, UpdateCollectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IValidator<UpdateCollectionRequest> _validator;
    private readonly ILogger<UpdateCollectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateCollectionHandler(
        ICollectionsRepository collectionsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IFileServiceClient fileServiceClient,
        IValidator<UpdateCollectionRequest> validator,
        ILogger<UpdateCollectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _fileServiceClient = fileServiceClient;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateCollectionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
            c => c.Id == command.CollectionId, cancellationToken);
        if (collectionResult.IsFailure)
            return collectionResult.Error;

        Collection collection = collectionResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(collection.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;
        Description? description = command.Request.Description is not null
            ? Description.Create(command.Request.Description).Value
            : null;
        var accessType = Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        AccessType previousAccessType = collection.AccessType;

        // Capture cover id BEFORE collection.Update().
        Guid? previousCoverId = collection.CoverImageId;
        long previousCoverBindingRevision = collection.CoverBindingRevision;

        UnitResult<Error> updateResult = collection.Update(title, description, accessType);
        if (updateResult.IsFailure)
            return updateResult.Error;

        TargetEntityDto target = new("collection", collection.Id);
        UnitResult<Error> coverSync = await MediaAssetSync.SyncSingleAssetAsync(
            _fileServiceClient,
            _outbox,
            previous: previousCoverId,
            previousBindingRevision: previousCoverBindingRevision,
            requested: command.Request.CoverId,
            target: target,
            actorUserId: _userScopedData.UserId,
            actorCanManageAnyAsset: _userScopedData.IsAdmin,
            attach: (id, revision) => collection.AttachImage(id, revision),
            detach: collection.DetachImage,
            cancellationToken);
        if (coverSync.IsFailure)
            return coverSync.Error;

        await _outbox.PublishAsync(new CollectionUpdated(collection.Id));

        if (previousAccessType != accessType)
        {
            await _outbox.PublishAsync(new CollectionAccessChanged(
                collection.Id,
                collection.AccessType.ToString(),
                collection.CourseId,
                collection.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Collection {CollectionId} updated (AccessType={AccessType})",
            collection.Id, collection.AccessType);

        return collection.Id;
    }
}
