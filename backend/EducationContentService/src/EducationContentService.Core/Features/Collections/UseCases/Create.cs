using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Collections;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
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

public sealed record CreateCollectionCommand(CreateCollectionRequest Request) : ICommand;

public sealed class CreateCollectionRequestValidator : AbstractValidator<CreateCollectionRequest>
{
    public CreateCollectionRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        When(x => x.Description is not null, () =>
        {
            RuleFor(x => x.Description!).MustBeValueObject(Description.Create);
        });
        When(x => x.AccessType is not null, () =>
        {
            RuleFor(x => x.AccessType!)
                .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
                .WithError(EducationErrors.InvalidAccessType());
        });
    }
}

public sealed class CreateCollectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("collections", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateCollectionRequest request,
                    [FromServices] CreateCollectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateCollectionCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class CreateCollectionHandler : ICommandHandler<Guid, CreateCollectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<CreateCollectionRequest> _validator;
    private readonly ILogger<CreateCollectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public CreateCollectionHandler(
        ICollectionsRepository collectionsRepository,
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<CreateCollectionRequest> validator,
        ILogger<CreateCollectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateCollectionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Course ownership check: only the course owner (or admin) may create a course-bound
        // collection. Space-level collections (CourseId=null) bypass — the caller owns by default.
        if (command.Request.CourseId is { } courseId)
        {
            Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
                c => c.Id == courseId, cancellationToken);
            if (courseResult.IsFailure)
                return courseResult.Error;

            UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
            if (ownership.IsFailure)
                return ownership.Error;
        }

        Title title = Title.Create(command.Request.Title).Value;

        AccessType? requestedAccessType = command.Request.AccessType is not null
            ? Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true)
            : null;

        if (requestedAccessType is not null)
        {
            UnitResult<Error> policyCheck = CollectionAccessPolicy.CanSetAccessType(
                requestedAccessType.Value, command.Request.CourseId);
            if (policyCheck.IsFailure)
                return policyCheck.Error;
        }

        var collection = new Collection(
            _userScopedData.UserId,
            title,
            command.Request.CourseId,
            requestedAccessType);

        if (command.Request.Description is not null)
        {
            Description description = Description.Create(command.Request.Description).Value;
            UnitResult<Error> updateResult = collection.Update(title, description, collection.AccessType);
            if (updateResult.IsFailure)
                return updateResult.Error;
        }

        await _collectionsRepository.AddAsync(collection, cancellationToken);

        await _outbox.PublishAsync(new CollectionCreated(
            collection.Id,
            collection.AccessType.ToString(),
            collection.CourseId,
            collection.AuthorId));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Collection {CollectionId} created by {AuthorId} (AccessType={AccessType}, CourseId={CourseId})",
            collection.Id, collection.AuthorId, collection.AccessType, collection.CourseId);

        return collection.Id;
    }
}
