using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Modules;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record TransferModuleItemCommand(
    Guid SourceModuleId, Guid ReferenceId, TransferModuleItemRequest Request) : ICommand;

public class TransferModuleItemRequestValidator : AbstractValidator<TransferModuleItemRequest>
{
    public TransferModuleItemRequestValidator()
    {
        RuleFor(x => x.TargetModuleId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("TargetModuleId"));
    }
}

public sealed class TransferModuleItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("modules/{sourceModuleId:guid}/items/{referenceId:guid}/transfer",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid sourceModuleId,
                [FromRoute] Guid referenceId,
                [FromBody] TransferModuleItemRequest request,
                [FromServices] TransferModuleItemHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new TransferModuleItemCommand(sourceModuleId, referenceId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

public sealed class TransferModuleItemHandler : ICommandHandler<Guid, TransferModuleItemCommand>
{
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<TransferModuleItemRequest> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<TransferModuleItemHandler> _logger;

    public TransferModuleItemHandler(
        IModuleItemsRepository moduleItemsRepository,
        IModulesRepository modulesRepository,
        IMaterialsRepository materialsRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        IQuizzesRepository quizzesRepository,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<TransferModuleItemRequest> validator,
        UserScopedData user,
        ILogger<TransferModuleItemHandler> logger)
    {
        _moduleItemsRepository = moduleItemsRepository;
        _modulesRepository = modulesRepository;
        _materialsRepository = materialsRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _quizzesRepository = quizzesRepository;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        TransferModuleItemCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        if (command.SourceModuleId == command.Request.TargetModuleId)
            return GeneralErrors.ValueIsInvalid("TargetModuleId");

        Result<ModuleItem, Error> itemResult = await _moduleItemsRepository.GetByAsync(
            mi => mi.ModuleId == command.SourceModuleId && mi.ReferenceId == command.ReferenceId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        ModuleItem sourceItem = itemResult.Value;

        Result<Module, Error> targetModuleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.Request.TargetModuleId, cancellationToken);
        if (targetModuleResult.IsFailure)
            return targetModuleResult.Error;

        const string ownershipSql = """
            SELECT c.author_id
            FROM course_items ci
            JOIN courses c ON c.id = ci.course_id
            WHERE ci.reference_id = @ModuleId AND ci.item_type = 'Module'
            LIMIT 1
            """;

        var connection = _transactionManager.GetDbConnection();

        // Both source and target modules are checked against the caller's ownership.
        // A module attached to a course inherits the course's author_id. An "orphan"
        // module (not yet attached to any course — e.g., a freshly created draft) has
        // no owner and is considered unclaimed. The endpoint is already gated by
        // Modules.MANAGE, so only authors/moderators/admins can reach here — allowing
        // transfers between orphan drafts is safe. If EITHER side is owned by someone
        // else, we reject.
        Guid? sourceAuthorId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(ownershipSql, new { ModuleId = command.SourceModuleId }, cancellationToken: cancellationToken));

        if (sourceAuthorId is not null)
        {
            UnitResult<Error> sourceOwnership = _user.CheckOwnership(sourceAuthorId);
            if (sourceOwnership.IsFailure)
                return sourceOwnership.Error;
        }

        Guid? targetAuthorId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(ownershipSql, new { ModuleId = command.Request.TargetModuleId }, cancellationToken: cancellationToken));

        if (targetAuthorId is not null)
        {
            UnitResult<Error> targetOwnership = _user.CheckOwnership(targetAuthorId);
            if (targetOwnership.IsFailure)
                return targetOwnership.Error;
        }

        // Compute sort key for the position in target module.
        // Note: referenceId is passed to ComputeMoveSortKey but is unused when after/before are provided.
        Result<SortKey, Error> sortKeyResult;
        if (command.Request.AfterSortKey is not null || command.Request.BeforeSortKey is not null)
        {
            sortKeyResult = await _moduleItemService.ComputeMoveSortKey(
                command.Request.TargetModuleId,
                command.ReferenceId,
                command.Request.AfterSortKey,
                command.Request.BeforeSortKey,
                cancellationToken);
        }
        else
        {
            SortKey appendKey =
                await _moduleItemService.ComputeAppendSortKeyAsync(command.Request.TargetModuleId, cancellationToken);

            sortKeyResult = appendKey;
        }

        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        // Delete + Insert in one transaction maintains the "one issue per module" invariant atomically.
        // We bypass ModuleItemService.CreateAsync() to avoid CheckIssueNotAttachedAsync which would
        // fail because the source ModuleItem still exists at this point in the transaction.
        _moduleItemsRepository.Delete(sourceItem);

        var newItem = new ModuleItem(
            command.Request.TargetModuleId,
            sourceItem.ItemType,
            sourceItem.ReferenceId,
            sortKeyResult.Value,
            sourceItem.IsOptional,
            sourceItem.ViewPriority);

        await _moduleItemsRepository.AddAsync(newItem, cancellationToken);

        // Resync Redis tags — material's course membership changed
        if (sourceItem.ItemType == ModuleItemType.Material)
        {
            Result<Domain.Materials.Material, Error> materialResult = await _materialsRepository.GetByAsync(
                m => m.Id == sourceItem.ReferenceId, cancellationToken);

            if (materialResult.IsSuccess)
            {
                List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(
                    sourceItem.ReferenceId, cancellationToken);

                await _outbox.PublishAsync(new MaterialAccessChanged(
                    sourceItem.ReferenceId,
                    materialResult.Value.AccessType.ToString(),
                    courseIds,
                    materialResult.Value.AuthorId));
            }
        }

        // ST-12 (#492): course_quizzes — derived из размещения в модулях. Перенос между
        // курсами добавляет привязку target-курсу (INV-4-зеркало) и снимает у source-курса,
        // если там не осталось других quiz-item'ов. Перенос внутри одного курса — no-op.
        if (sourceItem.ItemType == ModuleItemType.Quiz)
            await SyncCourseQuizBindingsAsync(sourceItem, command, cancellationToken);
        else if (sourceItem.ItemType == ModuleItemType.Issue)
            await _outbox.PublishAsync(new IssueAccessChanged(sourceItem.ReferenceId));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Item {ReferenceId} transferred from module {SourceModuleId} to module {TargetModuleId}",
            command.ReferenceId, command.SourceModuleId, command.Request.TargetModuleId);

        return newItem.Id;
    }

    private async Task SyncCourseQuizBindingsAsync(
        ModuleItem sourceItem, TransferModuleItemCommand command, CancellationToken cancellationToken)
    {
        Guid quizId = sourceItem.ReferenceId;
        Guid? sourceCourseId = await _modulesRepository.GetCourseIdAsync(
            command.SourceModuleId, cancellationToken);
        Guid? targetCourseId = await _modulesRepository.GetCourseIdAsync(
            command.Request.TargetModuleId, cancellationToken);

        if (sourceCourseId == targetCourseId)
            return; // тот же курс (или оба orphan) — привязки не меняются

        bool bindingsChanged = false;
        bool removedFromSource = false;

        if (targetCourseId is not null)
        {
            bindingsChanged |= await _courseQuizzesRepository.AddIfMissingAsync(
                targetCourseId.Value, quizId, cancellationToken);
        }

        if (sourceCourseId is not null)
        {
            // Source-строка module_items ещё в БД (delete не flushed) — исключаем по id.
            // Новый item уже в target-модуле и на source-курс не влияет.
            bool hasRemaining = await _moduleItemsRepository.HasOtherQuizItemsInCourseAsync(
                sourceCourseId.Value, quizId, excludedItemId: sourceItem.Id, cancellationToken);

            if (!hasRemaining)
            {
                Result<CourseQuiz, Error> bindingResult = await _courseQuizzesRepository.GetByAsync(
                    cq => cq.CourseId == sourceCourseId.Value && cq.QuizId == quizId,
                    cancellationToken: cancellationToken);

                if (bindingResult.IsSuccess)
                {
                    _courseQuizzesRepository.Delete(bindingResult.Value);
                    bindingsChanged = true;
                    removedFromSource = true;
                }
            }
        }

        if (!bindingsChanged)
            return;

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == quizId, cancellationToken);
        if (quizResult.IsFailure)
            return; // defensive: квиз исчез — Redis почистит quiz.hard_deleted

        // GetCourseIdsAsync читает БД — pending add/delete привязок учитываем вручную.
        List<Guid> courseIds = await _courseQuizzesRepository.GetCourseIdsAsync(quizId, cancellationToken);
        if (removedFromSource)
            courseIds.Remove(sourceCourseId!.Value);
        if (targetCourseId is not null && !courseIds.Contains(targetCourseId.Value))
            courseIds.Add(targetCourseId.Value);

        await _outbox.PublishAsync(new QuizAccessChanged(
            quizId,
            quizResult.Value.AccessType.ToString(),
            courseIds,
            quizResult.Value.AuthorId));

        _logger.LogInformation(
            "Quiz {QuizId} course bindings synced on transfer: source course {SourceCourseId} (removed: {Removed}), target course {TargetCourseId}",
            quizId, sourceCourseId, removedFromSource, targetCourseId);
    }
}
