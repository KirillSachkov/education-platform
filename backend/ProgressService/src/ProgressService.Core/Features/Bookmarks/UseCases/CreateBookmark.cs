using Common;
using ContentAccess;
using Core.Abstractions;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Core.Features.Bookmarks.UseCases;

public sealed record CreateBookmarkCommand(Guid CourseId, EntityType EntityType, Guid EntityId) : ICommand;

public sealed class CreateBookmarkCommandValidator : AbstractValidator<CreateBookmarkCommand>
{
    public CreateBookmarkCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateBookmarkCommand.CourseId)));
        RuleFor(x => x)
            .MustBeValueObject(command => BookmarkEntityReference.Of(command.EntityType, command.EntityId));
    }
}

public sealed class CreateBookmarkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/progress/courses/{courseId:guid}/bookmarks/{entityType}/{entityId:guid}",
                async Task<EndpointResult> (
                    Guid courseId,
                    EntityType entityType,
                    Guid entityId,
                    CreateBookmarkHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CreateBookmarkCommand(courseId, entityType, entityId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class CreateBookmarkHandler : ICommandHandler<CreateBookmarkCommand>
{
    private readonly IValidator<CreateBookmarkCommand> _validator;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;

    public CreateBookmarkHandler(
        IValidator<CreateBookmarkCommand> validator,
        IMaterialBookmarkRepository bookmarkRepository,
        IEducationContentServiceClient educationContentServiceClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData user)
    {
        _validator = validator;
        _bookmarkRepository = bookmarkRepository;
        _educationContentServiceClient = educationContentServiceClient;
        _entitlementChecker = entitlementChecker;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(CreateBookmarkCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<BookmarkEntityReference, Error> targetResult = BookmarkEntityReference.Of(command.EntityType, command.EntityId);
        if (targetResult.IsFailure)
        {
            return targetResult.Error;
        }

        BookmarkEntityReference target = targetResult.Value;

        Result<IReadOnlyCollection<ResolvedMaterialDto>, Error> resolveResult =
            await _educationContentServiceClient.ResolveMaterialTargetsAsync(
                new ResolveMaterialTargetsRequest(
                    [new MaterialResolveRequestItem(command.CourseId, new EntityReferenceDto(target.Type, target.Id))]),
                cancellationToken);

        if (resolveResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        bool targetResolved = resolveResult.Value.Any(
            x => x.CourseId == command.CourseId
                 && x.Target.Type == target.Type
                 && x.Target.Id == target.Id);
        if (!targetResolved)
        {
            return ProgressErrors.BookmarkTargetNotFound(target);
        }

        // Entitlement-check: букмарк требует доступ к ресурсу. Без него юзер мог
        // забукмарить чужой material/issue по прямому id (soft-leak — индикатор
        // существования + persistence в его inbox). Issue #82.
        AccessDecision access = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.FromEntityType(target.Type),
            target.Id,
            cancellationToken);
        if (!access.IsGranted)
        {
            return ProgressErrors.BookmarkTargetNotFound(target);
        }

        Result<MaterialBookmark, Error> bookmarkResult = MaterialBookmark.Create(_user.UserId, command.CourseId, target);
        if (bookmarkResult.IsFailure)
        {
            return bookmarkResult.Error;
        }

        await _bookmarkRepository.AddIfMissingAsync(bookmarkResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }
}
