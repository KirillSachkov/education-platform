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
using NotificationService.Domain.Subscriptions;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Subscriptions.UseCases;

public sealed class UnsubscribeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/notifications/subscriptions/{id:guid}", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid id,
                [FromServices] UnsubscribeHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new UnsubscribeCommand(id), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed class UnsubscribeValidator : AbstractValidator<UnsubscribeCommand>
{
    public UnsubscribeValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("subscriptionId"));
    }
}

public sealed record UnsubscribeCommand(Guid Id) : ICommand;

public sealed class UnsubscribeHandler : ICommandHandler<Guid, UnsubscribeCommand>
{
    private readonly ISubscriptionsRepository _subscriptionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UnsubscribeCommand> _validator;

    public UnsubscribeHandler(
        ISubscriptionsRepository subscriptionsRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UnsubscribeCommand> validator)
    {
        _subscriptionsRepository = subscriptionsRepository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(UnsubscribeCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        Guid userId = _user.UserId;
        SubscriptionId subscriptionId = SubscriptionId.Of(command.Id);

        // Targeted ownership check — раньше загружали ВСЕ подписки юзера и линейно искали
        // (issue #230, SEC-3). Idempotent: missing or owned by другим юзером → success
        // без раскрытия существования.
        Subscription? owned = await _subscriptionsRepository.GetBy(
            x => x.Id == subscriptionId && x.UserId == userId,
            cancellationToken);

        if (owned is null)
            return command.Id;

        await _subscriptionsRepository.RemoveAsync(SubscriptionId.Of(command.Id), cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        return command.Id;
    }
}
