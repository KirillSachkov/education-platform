using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Issues;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record UpdateIssueInternalMaterialsCommand(
    Guid IssueId,
    UpdateIssueInternalMaterialsRequest Request) : ICommand;

public class UpdateIssueInternalMaterialsRequestValidator
    : AbstractValidator<UpdateIssueInternalMaterialsRequest>
{
    public const int MAX_ITEMS = 50;

    public UpdateIssueInternalMaterialsRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .WithMessage("Список материалов обязателен")
            .Must(items => items is null || items.Count <= MAX_ITEMS)
            .WithMessage($"Количество материалов не может превышать {MAX_ITEMS}")
            .Must(items => items is null
                           || items.Select(x => x.ReferenceId).Distinct().Count() == items.Count)
            .WithMessage("Материалы не должны содержать дубликаты");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ReferenceId)
                .NotEmpty()
                .WithMessage("Идентификатор материала не может быть пустым");

            item.RuleFor(x => x.ItemType)
                .Must(t => string.Equals(t, nameof(ModuleItemType.Material), StringComparison.OrdinalIgnoreCase))
                .WithMessage("Тип материала должен быть Material");
        });
    }
}

public sealed class UpdateIssueInternalMaterialsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("issues/{issueId:guid}/internal-materials", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid issueId,
            [FromBody] UpdateIssueInternalMaterialsRequest request,
            [FromServices] UpdateIssueInternalMaterialsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(
                new UpdateIssueInternalMaterialsCommand(issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class UpdateIssueInternalMaterialsHandler
    : ICommandHandler<Guid, UpdateIssueInternalMaterialsCommand>
{
    private readonly IIssuesRepository _issuesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateIssueInternalMaterialsRequest> _validator;
    private readonly ILogger<UpdateIssueInternalMaterialsHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateIssueInternalMaterialsHandler(
        IIssuesRepository issuesRepository,
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IValidator<UpdateIssueInternalMaterialsRequest> validator,
        ILogger<UpdateIssueInternalMaterialsHandler> logger,
        UserScopedData userScopedData)
    {
        _issuesRepository = issuesRepository;
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateIssueInternalMaterialsCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(
            command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        Issue issue = issueResult.Value;

        Guid? courseAuthorId = await _issuesRepository.GetCourseAuthorIdAsync(command.IssueId, cancellationToken);
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseAuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Guid[] referenceIds = command.Request.Items.Select(x => x.ReferenceId).ToArray();
        if (referenceIds.Length > 0)
        {
            Dictionary<Guid, MaterialReferenceAccess> references = (await _materialsRepository
                    .GetReferenceAccessBatchAsync(referenceIds, cancellationToken))
                .ToDictionary(x => x.Id);

            bool hasInvalidReference = referenceIds.Any(id =>
                !references.TryGetValue(id, out MaterialReferenceAccess? row)
                || !IssueInternalMaterialPolicy.CanReference(
                    row.AuthorId, row.Status, row.AccessType, _userScopedData));
            if (hasInvalidReference)
                return GeneralErrors.ValueIsInvalid(nameof(command.Request.Items));
        }

        var materials = new List<IssueInternalMaterial>();
        foreach (var item in command.Request.Items)
        {
            var itemType = Enum.Parse<ModuleItemType>(item.ItemType, ignoreCase: true);

            Result<IssueInternalMaterial, Error> materialResult =
                IssueInternalMaterial.Create(itemType, item.ReferenceId, item.IsRequired);
            if (materialResult.IsFailure)
                return materialResult.Error;

            materials.Add(materialResult.Value);
        }

        issue.UpdateInternalMaterials(materials);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} internal materials updated ({Count} items)",
            command.IssueId, materials.Count);

        return issue.Id;
    }
}
