using Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Core.Features.Bookmarks.UseCases;

public sealed record DeleteBookmarkCommand(Guid CourseId, EntityType EntityType, Guid EntityId) : ICommand;

public sealed class DeleteBookmarkCommandValidator : AbstractValidator<DeleteBookmarkCommand>
{
    public DeleteBookmarkCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(DeleteBookmarkCommand.CourseId)));
        RuleFor(x => x)
            .MustBeValueObject(command => BookmarkEntityReference.Of(command.EntityType, command.EntityId));
    }
}

public sealed class DeleteBookmarkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/progress/courses/{courseId:guid}/bookmarks/{entityType}/{entityId:guid}",
                async Task<EndpointResult> (
                    Guid courseId,
                    EntityType entityType,
                    Guid entityId,
                    DeleteBookmarkHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new DeleteBookmarkCommand(courseId, entityType, entityId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class DeleteBookmarkHandler : ICommandHandler<DeleteBookmarkCommand>
{
    private readonly IValidator<DeleteBookmarkCommand> _validator;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public DeleteBookmarkHandler(
        IValidator<DeleteBookmarkCommand> validator,
        IMaterialBookmarkRepository bookmarkRepository,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _validator = validator;
        _bookmarkRepository = bookmarkRepository;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(DeleteBookmarkCommand command, CancellationToken cancellationToken)
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

        Guid userId = _user.UserId;
        Guid courseId = command.CourseId;
        BookmarkEntityReference targetRef = target;
        var existingBookmark = await _bookmarkRepository.GetByAsync(
            b => b.UserId == userId
                 && b.CourseId == courseId
                 && b.EntityReference.Type == targetRef.Type
                 && b.EntityReference.Id == targetRef.Id,
            cancellationToken);
        if (existingBookmark is null)
        {
            return UnitResult.Success<Error>();
        }

        _bookmarkRepository.Remove(existingBookmark);

        return await _transactionManager.SaveChangesAsync(cancellationToken);
    }
}
