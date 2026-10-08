using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain.Modules;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record UpdateModuleItemViewPriorityCommand(
    Guid ModuleId, Guid ReferenceId, UpdateModuleItemViewPriorityRequest Request) : ICommand;

public class UpdateModuleItemViewPriorityRequestValidator : AbstractValidator<UpdateModuleItemViewPriorityRequest>
{
    public UpdateModuleItemViewPriorityRequestValidator()
    {
        RuleFor(x => x.ViewPriority)
            .Must(v => Enum.TryParse<ViewPriority>(v, out _))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(UpdateModuleItemViewPriorityRequest.ViewPriority)));
    }
}

public sealed class UpdateModuleItemViewPriorityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("modules/{moduleId:guid}/items/{referenceId:guid}/priority",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid moduleId,
                [FromRoute] Guid referenceId,
                [FromBody] UpdateModuleItemViewPriorityRequest request,
                [FromServices] UpdateModuleItemViewPriorityHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new UpdateModuleItemViewPriorityCommand(moduleId, referenceId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class UpdateModuleItemViewPriorityHandler
    : ICommandHandler<Guid, UpdateModuleItemViewPriorityCommand>
{
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateModuleItemViewPriorityRequest> _validator;
    private readonly ILogger<UpdateModuleItemViewPriorityHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateModuleItemViewPriorityHandler(
        IModuleItemsRepository moduleItemsRepository,
        IModulesRepository modulesRepository,
        ITransactionManager transactionManager,
        IValidator<UpdateModuleItemViewPriorityRequest> validator,
        ILogger<UpdateModuleItemViewPriorityHandler> logger,
        UserScopedData userScopedData)
    {
        _moduleItemsRepository = moduleItemsRepository;
        _modulesRepository = modulesRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateModuleItemViewPriorityCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(moduleResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<ModuleItem, Error> itemResult = await _moduleItemsRepository.GetByAsync(
            mi => mi.ModuleId == command.ModuleId && mi.ReferenceId == command.ReferenceId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        ModuleItem item = itemResult.Value;

        var viewPriority = Enum.Parse<ViewPriority>(command.Request.ViewPriority);
        item.UpdateViewPriority(viewPriority);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "ViewPriority of item {ReferenceId} in module {ModuleId} set to {ViewPriority}",
            command.ReferenceId, command.ModuleId, viewPriority);

        return item.Id;
    }
}
