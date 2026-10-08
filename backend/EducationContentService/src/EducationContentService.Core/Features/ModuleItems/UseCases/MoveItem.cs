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
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record MoveModuleItemCommand(
    Guid ModuleId, Guid ReferenceId, MoveModuleItemRequest Request) : ICommand;

public class MoveModuleItemRequestValidator : AbstractValidator<MoveModuleItemRequest>
{
    public MoveModuleItemRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.AfterSortKey is not null || x.BeforeSortKey is not null)
            .WithError(GeneralErrors.ValueIsInvalid("AfterSortKey/BeforeSortKey"));
    }
}

public sealed class MoveModuleItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("modules/{moduleId:guid}/items/{referenceId:guid}/move",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid moduleId,
                [FromRoute] Guid referenceId,
                [FromBody] MoveModuleItemRequest request,
                [FromServices] MoveModuleItemHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new MoveModuleItemCommand(moduleId, referenceId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class MoveModuleItemHandler : ICommandHandler<Guid, MoveModuleItemCommand>
{
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MoveModuleItemRequest> _validator;
    private readonly ILogger<MoveModuleItemHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public MoveModuleItemHandler(
        IModuleItemsRepository moduleItemsRepository,
        IModulesRepository modulesRepository,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IValidator<MoveModuleItemRequest> validator,
        ILogger<MoveModuleItemHandler> logger,
        UserScopedData userScopedData)
    {
        _moduleItemsRepository = moduleItemsRepository;
        _modulesRepository = modulesRepository;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        MoveModuleItemCommand command, CancellationToken cancellationToken)
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

        Result<SortKey, Error> sortKeyResult = await _moduleItemService.ComputeMoveSortKey(
            command.ModuleId, command.ReferenceId,
            command.Request.AfterSortKey, command.Request.BeforeSortKey,
            cancellationToken);
        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        item.UpdateSortKey(sortKeyResult.Value);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Item {ReferenceId} moved in module {ModuleId}",
            command.ReferenceId, command.ModuleId);

        return item.Id;
    }
}
