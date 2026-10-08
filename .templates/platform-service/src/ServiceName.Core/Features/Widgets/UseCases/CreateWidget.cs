using Core.Abstractions;
using Core.Database;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ServiceName.Contracts;
using ServiceName.Core.Database;
using ServiceName.Domain.Widgets;

namespace ServiceName.Core.Features.Widgets.UseCases;

/// <summary>
/// Vertical slice: command + validator + endpoint + handler in one file.
/// Replace "Widgets" with your own aggregate name.
/// </summary>
public sealed record CreateWidgetCommand(Guid OwnerId, string Name) : ICommand;

public sealed class CreateWidgetValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(WidgetName.MAX_LENGTH);
    }
}

public sealed class CreateWidgetEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/widgets", HandleAsync)
            .RequirePermissions("widgets.manage");

    private static async Task<EndpointResult<Guid>> HandleAsync(
        [FromBody] CreateWidgetRequest request,
        [FromServices] CreateWidgetHandler handler,
        [FromServices] UserScopedData user,
        CancellationToken ct) =>
        await handler.Handle(new CreateWidgetCommand(user.UserId, request.Name), ct);
}

public sealed class CreateWidgetHandler : ICommandHandler<Guid, CreateWidgetCommand>
{
    private readonly IWidgetsRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<CreateWidgetCommand> _validator;
    private readonly ILogger<CreateWidgetHandler> _logger;

    public CreateWidgetHandler(
        IWidgetsRepository repository,
        ITransactionManager transactionManager,
        IValidator<CreateWidgetCommand> validator,
        ILogger<CreateWidgetHandler> logger)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(CreateWidgetCommand command, CancellationToken cancellationToken)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return Error.Validation("widget.validation.failed", validation.Errors[0].ErrorMessage);

        Result<WidgetName, Error> nameResult = WidgetName.Of(command.Name);
        if (nameResult.IsFailure)
            return nameResult.Error;

        Result<Widget, Error> widgetResult = Widget.Create(command.OwnerId, nameResult.Value);
        if (widgetResult.IsFailure)
            return widgetResult.Error;

        await _repository.AddAsync(widgetResult.Value, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Widget {WidgetId} created by {OwnerId}", widgetResult.Value.Id, command.OwnerId);
        return widgetResult.Value.Id;
    }
}
