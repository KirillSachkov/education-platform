using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Inbox.UseCases;

public sealed class MarkAsReadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/{id:guid}/read", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid id,
                [FromServices] MarkAsReadHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new MarkAsReadCommand(id), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed class MarkAsReadValidator : AbstractValidator<MarkAsReadCommand>
{
    public MarkAsReadValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("notificationId"));
    }
}

public sealed record MarkAsReadCommand(Guid Id) : ICommand;

public sealed class MarkAsReadHandler : ICommandHandler<Guid, MarkAsReadCommand>
{
    private readonly INotificationsRepository _notificationsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<MarkAsReadCommand> _validator;

    public MarkAsReadHandler(
        INotificationsRepository notificationsRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<MarkAsReadCommand> validator)
    {
        _notificationsRepository = notificationsRepository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(MarkAsReadCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        NotificationId notificationId = NotificationId.Of(command.Id);
        Guid userId = _user.UserId;

        Result<Notification, Error> notificationResult = await _notificationsRepository.GetBy(
            x => x.Id == notificationId && x.RecipientUserId == userId,
            cancellationToken);

        if (notificationResult.IsFailure)
            return notificationResult.Error;

        Notification notification = notificationResult.Value;

        // Idempotent: MarkAsRead returns false if already read — still success to the caller.
        bool newlyRead = notification.MarkAsRead();

        if (newlyRead)
        {
            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        return command.Id;
    }
}
