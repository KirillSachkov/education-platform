using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain.Modules;
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

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record UpdateModuleCommand(Guid ModuleId, UpdateModuleRequest Request) : ICommand;

public class UpdateModuleRequestValidator : AbstractValidator<UpdateModuleRequest>
{
    public UpdateModuleRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Description).MustBeValueObject(v => Description.Create(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
        When(x => x.DetailedDescription is not null, () =>
        {
            RuleFor(x => x.DetailedDescription!)
                .MustBeValueObject(DetailedDescription.Create);
        });
    }
}

public sealed class UpdateModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("modules/{moduleId:guid}", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid moduleId,
            [FromBody] UpdateModuleRequest request,
            [FromServices] UpdateModuleHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new UpdateModuleCommand(moduleId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class UpdateModuleHandler : ICommandHandler<Guid, UpdateModuleCommand>
{
    private readonly IModulesRepository _modulesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<UpdateModuleRequest> _validator;
    private readonly ILogger<UpdateModuleHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateModuleHandler(
        IModulesRepository modulesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<UpdateModuleRequest> validator,
        ILogger<UpdateModuleHandler> logger,
        UserScopedData userScopedData)
    {
        _modulesRepository = modulesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateModuleCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        Module module = moduleResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(module.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;
        Description? description = string.IsNullOrWhiteSpace(command.Request.Description)
            ? null
            : Description.Create(command.Request.Description).Value;
        DetailedDescription? detailedDescription = string.IsNullOrWhiteSpace(command.Request.DetailedDescription)
            ? null
            : DetailedDescription.Create(command.Request.DetailedDescription).Value;

        module.Update(title, description, detailedDescription);

        await _outbox.PublishAsync(new ModuleUpdated(module.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Module {ModuleId} updated", command.ModuleId);

        return module.Id;
    }
}
