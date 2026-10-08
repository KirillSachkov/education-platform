using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Modules;

namespace ProgressService.Core.Services;

public sealed class ModuleProgressService : IModuleProgressService
{
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IModuleProgressRepository _moduleProgressRepository;

    public ModuleProgressService(
        IModuleItemProgressRepository moduleItemProgressRepository,
        IModuleProgressRepository moduleProgressRepository)
    {
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _moduleProgressRepository = moduleProgressRepository;
    }

    public async Task<UnitResult<Error>> CompleteMaterialModuleItemAsync(
        Guid enrollmentId,
        Guid moduleId,
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        // Идемпотентно: если item уже существует (ранее создан при cascade или при повторной
        // синхронизации), не создаём дубликат. Повторный просмотр материала в разных
        // enrollment'ах приходит через cascade — handler должен корректно пропускать
        // уже отмеченные позиции.
        bool alreadyExists = await _moduleItemProgressRepository.ExistsAsync(
            x => x.EnrollmentId == enrollmentId
                 && x.ModuleId == moduleId
                 && x.ReferenceId == materialId
                 && x.ItemType == ModuleItemProgressType.MATERIAL,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleProgress, Error> moduleProgressResult = await _moduleProgressRepository
            .GetByAsync(x => x.EnrollmentId == enrollmentId && x.ModuleId == moduleId, cancellationToken);
        if (moduleProgressResult.IsNotFound())
        {
            return ProgressErrors.ModuleProgressNotStarted();
        }
        if (moduleProgressResult.IsFailure)
        {
            return moduleProgressResult.Error;
        }

        Result<ModuleItemProgress, Error> createModuleItemProgressResult = ModuleItemProgress.CreateMaterialProgress(
            enrollmentId,
            moduleId,
            materialId);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        await _moduleItemProgressRepository.AddAsync(createModuleItemProgressResult.Value, cancellationToken);

        return CompleteModuleItemAsync(createModuleItemProgressResult.Value, moduleProgressResult.Value);
    }

    public async Task<UnitResult<Error>> CompleteQuizModuleItemAsync(
        Guid enrollmentId,
        Guid moduleId,
        Guid quizId,
        CancellationToken cancellationToken = default)
    {
        // Зеркало CompleteMaterialModuleItemAsync (ST-13 #493). Идемпотентно: повторная
        // passed-попытка квиза в этом enrollment'е не создаёт дубликат и не двигает счётчик.
        bool alreadyExists = await _moduleItemProgressRepository.ExistsAsync(
            x => x.EnrollmentId == enrollmentId
                 && x.ModuleId == moduleId
                 && x.ReferenceId == quizId
                 && x.ItemType == ModuleItemProgressType.QUIZ,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleProgress, Error> moduleProgressResult = await _moduleProgressRepository
            .GetByAsync(x => x.EnrollmentId == enrollmentId && x.ModuleId == moduleId, cancellationToken);
        if (moduleProgressResult.IsNotFound())
        {
            return ProgressErrors.ModuleProgressNotStarted();
        }
        if (moduleProgressResult.IsFailure)
        {
            return moduleProgressResult.Error;
        }

        Result<ModuleItemProgress, Error> createModuleItemProgressResult = ModuleItemProgress.CreateQuizProgress(
            enrollmentId,
            moduleId,
            quizId);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        await _moduleItemProgressRepository.AddAsync(createModuleItemProgressResult.Value, cancellationToken);

        return CompleteModuleItemAsync(createModuleItemProgressResult.Value, moduleProgressResult.Value);
    }

    public async Task<UnitResult<Error>> EnsureModuleProgressAsync(
        Guid enrollmentId,
        Guid moduleId,
        int moduleItemsTotal,
        CancellationToken cancellationToken = default)
    {
        Result<ModuleProgress, Error> existing = await _moduleProgressRepository.GetByAsync(
            p => p.EnrollmentId == enrollmentId && p.ModuleId == moduleId,
            cancellationToken);

        if (existing.IsSuccess)
        {
            // Sync ItemsTotal к актуальному ECS-значению — компенсирует отсутствие реактивного
            // sync на attach/detach/move module items (issue #199). Без этого ItemsTotal
            // фиксируется при первом касании и может разойтись с реальной структурой модуля.
            existing.Value.SyncItemsTotal(moduleItemsTotal);
            return UnitResult.Success<Error>();
        }

        if (!existing.IsNotFound())
        {
            return existing.Error;
        }

        Result<ModuleProgress, Error> createResult = ModuleProgress.Create(
            enrollmentId,
            moduleId,
            moduleItemsTotal);
        if (createResult.IsFailure)
        {
            return createResult.Error;
        }

        await _moduleProgressRepository.AddAsync(createResult.Value, cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> CompleteIssueModuleItemAsync(
        Guid enrollmentId,
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        Result<ModuleItemProgress, Error> moduleItemProgressResult = await _moduleItemProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentId
                    && x.ReferenceId == issueId
                    && x.ItemType == ModuleItemProgressType.ISSUE,
                cancellationToken);

        if (moduleItemProgressResult.IsNotFound())
        {
            return UnitResult.Success<Error>();
        }
        if (moduleItemProgressResult.IsFailure)
        {
            return moduleItemProgressResult.Error;
        }

        Result<ModuleProgress, Error> moduleProgressResult = await _moduleProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentId && x.ModuleId == moduleItemProgressResult.Value.ModuleId,
                cancellationToken);
        if (moduleProgressResult.IsNotFound())
        {
            return UnitResult.Success<Error>();
        }
        if (!moduleProgressResult.IsFailure)
        {
            return CompleteModuleItemAsync(moduleItemProgressResult.Value, moduleProgressResult.Value);
        }

        return moduleProgressResult.Error;
    }
    private static UnitResult<Error> CompleteModuleItemAsync(
        ModuleItemProgress moduleItemProgress,
        ModuleProgress moduleProgress)
    {
        Result<bool, Error> markCompletedResult = moduleItemProgress.MarkCompleted();
        if (markCompletedResult.IsFailure)
        {
            return markCompletedResult.Error;
        }

        if (!markCompletedResult.Value)
        {
            return UnitResult.Success<Error>();
        }

        UnitResult<Error> markItemCompletedResult = moduleProgress.MarkItemCompleted();
        if (markItemCompletedResult.IsFailure)
        {
            return markItemCompletedResult.Error;
        }

        return moduleProgress.TryCompleteModule();
    }

    public Task<UnitResult<Error>> UncompleteMaterialModuleItemAsync(
        Guid enrollmentId,
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        UncompleteModuleItemAsync(
            enrollmentId,
            materialId,
            ModuleItemProgressType.MATERIAL,
            cancellationToken);

    public Task<UnitResult<Error>> UncompleteIssueModuleItemAsync(
        Guid enrollmentId,
        Guid issueId,
        CancellationToken cancellationToken = default) =>
        UncompleteModuleItemAsync(
            enrollmentId,
            issueId,
            ModuleItemProgressType.ISSUE,
            cancellationToken);

    private async Task<UnitResult<Error>> UncompleteModuleItemAsync(
        Guid enrollmentId,
        Guid referenceId,
        ModuleItemProgressType itemType,
        CancellationToken cancellationToken)
    {
        Result<ModuleItemProgress, Error> itemResult = await _moduleItemProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentId
                    && x.ReferenceId == referenceId
                    && x.ItemType == itemType,
                cancellationToken);

        if (itemResult.IsNotFound())
        {
            return UnitResult.Success<Error>();
        }

        if (itemResult.IsFailure)
        {
            return itemResult.Error;
        }

        ModuleItemProgress item = itemResult.Value;
        if (!item.MarkUncompleted())
        {
            // Уже NOT_COMPLETED — нечего откатывать на уровне модуля.
            return UnitResult.Success<Error>();
        }

        Result<ModuleProgress, Error> moduleResult = await _moduleProgressRepository
            .GetByAsync(x => x.EnrollmentId == enrollmentId && x.ModuleId == item.ModuleId, cancellationToken);

        if (moduleResult.IsNotFound())
        {
            return UnitResult.Success<Error>();
        }

        if (moduleResult.IsFailure)
        {
            return moduleResult.Error;
        }

        return moduleResult.Value.MarkItemUncompleted();
    }
}
