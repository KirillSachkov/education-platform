using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.ProgressLookup;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;

namespace ProgressService.Core.Services;

/// <summary>
///     Shared staff-completion логика (#398, #518). Извлечена из <c>MarkIssueCompleteForUserHandler</c>,
///     чтобы и ручная приёмка (#398), и staff-override статуса с target=COMPLETED (#518) шли через
///     один код без дублирования двухфазного flush'а и синтетического submission'а. Подробности —
///     <see cref="IStaffIssueCompletionService"/>.
/// </summary>
public sealed class StaffIssueCompletionService : IStaffIssueCompletionService
{
    /// <summary>
    ///     Sentinel-маркер payload синтетического submission'а: студент не сдавал реальную работу,
    ///     задачу принял автор/админ/модератор вручную. Домен (<see cref="IssueSubmissionPayload"/>)
    ///     требует absolute http(s) URL на allowed-домене — sachkov-learn.net подходит.
    /// </summary>
    private const string MANUAL_COMPLETION_PAYLOAD = "https://sachkov-learn.net/manual-completion";

    private readonly IEnrollmentAnchorService _enrollmentAnchorService;
    private readonly IProjectProgressRepository _projectProgressRepository;
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly IIssueSubmissionRepository _issueSubmissionRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IModuleProgressService _moduleProgressService;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<StaffIssueCompletionService> _logger;

    public StaffIssueCompletionService(
        IEnrollmentAnchorService enrollmentAnchorService,
        IProjectProgressRepository projectProgressRepository,
        IIssueProgressRepository issueProgressRepository,
        IIssueSubmissionRepository issueSubmissionRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IModuleProgressService moduleProgressService,
        IEducationContentServiceClient ecsClient,
        ITransactionManager transactionManager,
        ILogger<StaffIssueCompletionService> logger)
    {
        _enrollmentAnchorService = enrollmentAnchorService;
        _projectProgressRepository = projectProgressRepository;
        _issueProgressRepository = issueProgressRepository;
        _issueSubmissionRepository = issueSubmissionRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _moduleProgressService = moduleProgressService;
        _ecsClient = ecsClient;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> CompleteIssueForUserAsync(
        Guid studentId,
        Guid courseId,
        Guid issueId,
        Guid reviewerId,
        IssueReviewFeedback? feedback,
        CourseDto courseDetail,
        CancellationToken cancellationToken)
    {
        if (reviewerId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(reviewerId));
        }

        // Резолвим контекст задачи из ECS: projectId — по issueId (detail), moduleId + totals —
        // по lightweight lookup'ам. Нужны, чтобы построить ту же progress-цепочку, что и
        // StartIssueWork: каскад Approve() требует ProjectProgress (и ModuleProgress если задача
        // в модуле), иначе вся транзакция откатывается.
        Result<IssueDetailDto, Error> issueDetailResult = await _ecsClient
            .GetDetailIssueByIdAsync(issueId, cancellationToken);
        if (issueDetailResult.IsFailure)
        {
            return issueDetailResult.Error;
        }

        Guid projectId = issueDetailResult.Value.ProjectId;

        Result<IssueDto, Error> issueLookupResult = await _ecsClient
            .GetIssueLookupAsync(projectId, issueId, cancellationToken);
        if (issueLookupResult.IsFailure)
        {
            return issueLookupResult.Error;
        }

        Guid? moduleId = issueLookupResult.Value.ModuleId;

        Result<ProjectDto, Error> projectLookupResult = await _ecsClient
            .GetProjectLookupAsync(courseId, projectId, cancellationToken);
        if (projectLookupResult.IsFailure)
        {
            return projectLookupResult.Error;
        }

        // Lazy progress-anchor для (студент, курс).
        Result<CourseEnrollment, Error> enrollmentResult = await _enrollmentAnchorService
            .EnsureEnrollmentAsync(
                studentId,
                courseId,
                courseDetail.AuthorId,
                EnrollmentSource.ENGAGEMENT,
                cancellationToken);
        if (enrollmentResult.IsFailure)
        {
            return enrollmentResult.Error;
        }

        Guid enrollmentId = enrollmentResult.Value.Id;

        // ProjectProgress обязателен для каскада Approve() (см. ProgressService CLAUDE.md).
        UnitResult<Error> ensureProjectProgressResult = await EnsureProjectProgressAsync(
            enrollmentId,
            projectId,
            projectLookupResult.Value.ProjectIssuesTotal,
            cancellationToken);
        if (ensureProjectProgressResult.IsFailure)
        {
            return ensureProjectProgressResult.Error;
        }

        // ModuleProgress + ModuleItemProgress нужны, чтобы каскад CompleteModuleItemOnIssueApproved
        // не упал на ModuleProgressNotStarted и завершил элемент модуля.
        if (moduleId is not null)
        {
            Result<ModuleDto, Error> moduleLookupResult = await _ecsClient
                .GetModuleLookupAsync(courseId, moduleId.Value, cancellationToken);
            if (moduleLookupResult.IsFailure)
            {
                return moduleLookupResult.Error;
            }

            UnitResult<Error> ensureModuleProgressResult = await _moduleProgressService.EnsureModuleProgressAsync(
                enrollmentId,
                moduleId.Value,
                moduleLookupResult.Value.ModuleItemsTotal,
                cancellationToken);
            if (ensureModuleProgressResult.IsFailure)
            {
                return ensureModuleProgressResult.Error;
            }

            UnitResult<Error> ensureModuleItemProgressResult = await EnsureIssueModuleItemProgressAsync(
                enrollmentId,
                issueId,
                moduleId.Value,
                cancellationToken);
            if (ensureModuleItemProgressResult.IsFailure)
            {
                return ensureModuleItemProgressResult.Error;
            }
        }

        // IssueProgress (одна строка на пару enrollment+issue) — создаём, если студент не касался задачи.
        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(
                x => x.EnrollmentId == enrollmentId && x.IssueId == issueId,
                cancellationToken);
        if (issueProgressResult.IsFailureExceptNotFound())
        {
            return issueProgressResult.Error;
        }

        IssueProgress issueProgress;
        if (issueProgressResult.IsNotFound())
        {
            Result<IssueProgress, Error> createIssueProgressResult = IssueProgress.Create(
                enrollmentId,
                projectId,
                issueId);
            if (createIssueProgressResult.IsFailure)
            {
                return createIssueProgressResult.Error;
            }

            issueProgress = createIssueProgressResult.Value;
            await _issueProgressRepository.AddAsync(issueProgress, cancellationToken);
        }
        else
        {
            issueProgress = issueProgressResult.Value;
        }

        // Если задача уже принята по другой попытке — project/module начислены. Не дублируем
        // каскад: создаём синтетический submission уже как APPROVED без approve-event (cascadeProgress=false).
        bool issueAlreadyComplete = issueProgress.Status == IssueProgressStatus.COMPLETED;

        // Каскад Approve() требует UNDER_REVIEW — нормализуем shared issue-progress сюда из любого
        // не-завершённого статуса (no-op если уже UNDER_REVIEW / COMPLETED).
        UnitResult<Error> ensureUnderReview = issueProgress.EnsureUnderReview();
        if (ensureUnderReview.IsFailure)
        {
            return ensureUnderReview.Error;
        }

        // Синтетический submission: студент не сдавал реальную работу, поэтому payload — sentinel-маркер
        // ручной приёмки. attemptNumber = max+1 (на случай если попытки всё же были, например после
        // reopen — здесь обычно их нет, поэтому 1).
        Result<int, Error> maxAttemptNumberResult = await _issueSubmissionRepository
            .GetMaxAttemptNumberAsync(issueProgress.Id, cancellationToken);
        if (maxAttemptNumberResult.IsFailure)
        {
            return maxAttemptNumberResult.Error;
        }

        Result<AttemptNumber, Error> attemptNumberResult = AttemptNumber.Create(maxAttemptNumberResult.Value + 1);
        if (attemptNumberResult.IsFailure)
        {
            return attemptNumberResult.Error;
        }

        Result<IssueSubmissionPayload, Error> payloadResult = IssueSubmissionPayload.Create(MANUAL_COMPLETION_PAYLOAD);
        if (payloadResult.IsFailure)
        {
            return payloadResult.Error;
        }

        // raiseAwaitingReview: false — синтетический submission сразу принимается; awaiting-review
        // сигнал (AI-проверка + уведомление автору «студент сдал») здесь не нужен и был бы вреден.
        Result<IssueSubmission, Error> submissionResult = IssueSubmission.Create(
            issueProgress.Id,
            attemptNumberResult.Value,
            payloadResult.Value,
            raiseAwaitingReview: false);
        if (submissionResult.IsFailure)
        {
            return submissionResult.Error;
        }

        IssueSubmission submission = submissionResult.Value;
        await _issueSubmissionRepository.AddAsync(submission, cancellationToken);

        // Флэшим всю progress-цепочку (enrollment-anchor + ProjectProgress + ModuleProgress +
        // ModuleItemProgress + IssueProgress + PENDING submission) ДО approve-каскада. Каскад
        // (ApproveIssueProgressOnSubmissionApproved / UpdateProjectProgressOnIssueApproved / …)
        // резолвит эти строки через DB GetByAsync, который НЕ видит ещё не закоммиченные Added-сущности
        // (тот же паттерн, что в EnrollmentAnchorService). Без предварительного flush'а каскад падает
        // на issue.progress.not.found. SaveChanges не диспатчит здесь approve-event — submission ещё PENDING.
        UnitResult<Error> chainSaveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (chainSaveResult.IsFailure)
        {
            return chainSaveResult.Error;
        }

        UnitResult<Error> forceApproveResult = submission.ForceApprove(
            reviewerId,
            feedback,
            cascadeProgress: !issueAlreadyComplete);
        if (forceApproveResult.IsFailure)
        {
            return forceApproveResult.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Issue manually completed for student. UserId: {UserId}, ReviewerId: {ReviewerId}, CourseId: {CourseId}, IssueId: {IssueId}, AlreadyComplete: {AlreadyComplete}",
            studentId,
            reviewerId,
            courseId,
            issueId,
            issueAlreadyComplete);

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> EnsureProjectProgressAsync(
        Guid enrollmentId,
        Guid projectId,
        int projectIssuesTotal,
        CancellationToken cancellationToken)
    {
        bool alreadyExists = await _projectProgressRepository.ExistsAsync(
            p => p.EnrollmentId == enrollmentId && p.ProjectId == projectId,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ProjectProgress, Error> createProjectProgressResult = ProjectProgress.Create(
            enrollmentId,
            projectId,
            projectIssuesTotal);
        if (createProjectProgressResult.IsFailure)
        {
            return createProjectProgressResult.Error;
        }

        await _projectProgressRepository.AddAsync(createProjectProgressResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }

    private async Task<UnitResult<Error>> EnsureIssueModuleItemProgressAsync(
        Guid enrollmentId,
        Guid issueId,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        bool alreadyExists = await _moduleItemProgressRepository.ExistsAsync(
            x => x.EnrollmentId == enrollmentId
                 && x.ModuleId == moduleId
                 && x.ReferenceId == issueId
                 && x.ItemType == ModuleItemProgressType.ISSUE,
            cancellationToken);

        if (alreadyExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<ModuleItemProgress, Error> createModuleItemProgressResult = ModuleItemProgress.CreateIssueProgress(
            enrollmentId,
            moduleId,
            issueId);
        if (createModuleItemProgressResult.IsFailure)
        {
            return createModuleItemProgressResult.Error;
        }

        await _moduleItemProgressRepository.AddAsync(createModuleItemProgressResult.Value, cancellationToken);

        return UnitResult.Success<Error>();
    }
}