using CSharpFunctionalExtensions;
using ProgressService.Domain.Issues.Events;
using SharedKernel;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues;

/// <summary>
/// Детальный прогресс пользователя по задаче.
/// Хранит текущее состояние workflow задачи отдельно от истории ее проверок.
/// </summary>
public sealed class IssueProgress : AggregateRoot
{
    private IssueProgress(Guid enrollmentId, Guid projectId, Guid issueId)
    {
        Id = Guid.CreateVersion7();
        EnrollmentId = enrollmentId;
        ProjectId = projectId;
        IssueId = issueId;
        Status = IssueProgressStatus.NOT_STARTED;
    }

    private IssueProgress()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid EnrollmentId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid IssueId { get; private set; }

    public IssueProgressStatus Status { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public static Result<IssueProgress, Error> Create(Guid enrollmentId, Guid projectId, Guid issueId)
    {
        if (enrollmentId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(enrollmentId));
        }

        if (projectId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(projectId));
        }

        if (issueId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(issueId));
        }

        return new IssueProgress(enrollmentId, projectId, issueId);
    }

    public UnitResult<Error> StartWork()
    {
        if (Status == IssueProgressStatus.IN_PROGRESS)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != IssueProgressStatus.NOT_STARTED)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(StartWork));
        }

        Status = IssueProgressStatus.IN_PROGRESS;
        StartedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> SubmitForReview()
    {
        if (Status != IssueProgressStatus.IN_PROGRESS &&
            Status != IssueProgressStatus.REQUESTED_CHANGES)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(SubmitForReview));
        }

        Status = IssueProgressStatus.UNDER_REVIEW;
        StartedAt ??= DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RequestChanges()
    {
        if (Status != IssueProgressStatus.UNDER_REVIEW)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(RequestChanges));
        }

        Status = IssueProgressStatus.REQUESTED_CHANGES;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Approve()
    {
        if (Status != IssueProgressStatus.UNDER_REVIEW)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(Approve));
        }

        Status = IssueProgressStatus.COMPLETED;
        CompletedAt = DateTime.UtcNow;

        RaiseDomainEvent(new IssueProgressApprovedEvent(this));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Возвращает IssueProgress из терминального статуса (COMPLETED или REQUESTED_CHANGES) в UNDER_REVIEW.
    /// Если переход из COMPLETED — поднимает <see cref="IssueProgressReopenedEvent"/> для отката XP,
    /// project и module прогресса.
    /// <para>
    /// <b>UNDER_REVIEW допустим как идемпотентный no-op (#383).</b> IssueProgress шарится между всеми
    /// попытками задачи (одна строка на пару enrollment+issue), тогда как каждая <see cref="IssueSubmissions.IssueSubmission"/>
    /// — отдельная попытка. Когда ревьюер reopen'ит старую APPROVED/CHANGES_REQUESTED попытку, а более
    /// новая попытка (или AI-гейт) уже перевела shared-progress в UNDER_REVIEW — каскад
    /// <c>ReopenIssueProgressOnSubmissionReviewReopened</c> не должен падать. Прогресс уже там, где надо;
    /// дополнительный переход и event не нужны.
    /// </para>
    /// </summary>
    public UnitResult<Error> ReopenReview()
    {
        // Уже under review (более новая попытка / AI-гейт продвинули shared-progress) — no-op.
        if (Status == IssueProgressStatus.UNDER_REVIEW)
        {
            return UnitResult.Success<Error>();
        }

        if (Status != IssueProgressStatus.COMPLETED && Status != IssueProgressStatus.REQUESTED_CHANGES)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(ReopenReview));
        }

        bool wasCompleted = Status == IssueProgressStatus.COMPLETED;

        Status = IssueProgressStatus.UNDER_REVIEW;
        CompletedAt = null;

        if (wasCompleted)
        {
            RaiseDomainEvent(new IssueProgressReopenedEvent(this));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Приводит IssueProgress в статус UNDER_REVIEW из любого не-завершённого состояния, чтобы
    /// последующий <see cref="Approve"/> прошёл (Approve требует UNDER_REVIEW). Идемпотентно: уже
    /// UNDER_REVIEW → no-op. Используется ручной приёмкой автора/админа («Отметить выполненным», #383),
    /// которая форс-аппрувит попытку независимо от текущего статуса проверки. Если уже COMPLETED —
    /// возвращает success без перехода (caller сам решает не вызывать повторно Approve).
    /// Никаких событий не поднимает — это подготовительный normalize, событие отката (Reopened)
    /// здесь неуместно.
    /// </summary>
    public UnitResult<Error> EnsureUnderReview()
    {
        if (Status == IssueProgressStatus.UNDER_REVIEW || Status == IssueProgressStatus.COMPLETED)
        {
            return UnitResult.Success<Error>();
        }

        Status = IssueProgressStatus.UNDER_REVIEW;
        StartedAt ??= DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Сбрасывает прогресс задачи в NOT_STARTED — staff-override (#518). Чистит
    ///     <see cref="StartedAt"/> и <see cref="CompletedAt"/>. Если уходим из COMPLETED — поднимает
    ///     <see cref="IssueProgressReopenedEvent"/> для отката XP / project / module прогресса
    ///     (тот же откат-каскад, что у <see cref="ReopenReview"/>). Идемпотентно: уже NOT_STARTED →
    ///     no-op (события не дублируются).
    /// </summary>
    public UnitResult<Error> Reset()
    {
        if (Status == IssueProgressStatus.NOT_STARTED)
        {
            return UnitResult.Success<Error>();
        }

        bool wasCompleted = Status == IssueProgressStatus.COMPLETED;

        Status = IssueProgressStatus.NOT_STARTED;
        StartedAt = null;
        CompletedAt = null;

        if (wasCompleted)
        {
            RaiseDomainEvent(new IssueProgressReopenedEvent(this));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Ручной staff-override статуса задачи (#518): автор/админ/модератор может выставить ЛЮБОЙ
    ///     статус прогресса студента, минуя обычный workflow (submit → review → approve). Покрывает
    ///     полную палитру кроме терминалов, у которых есть собственные интенциональные методы:
    ///     <list type="bullet">
    ///       <item>NOT_STARTED → <see cref="Reset"/>;</item>
    ///       <item>COMPLETED → синтетический <c>IssueSubmission.ForceApprove</c> в use-case'е
    ///       (нужен submission для integration event + истории ревью), сюда НЕ попадает.</item>
    ///     </list>
    ///     <para>
    ///     Инвариант событий: <see cref="IssueProgressReopenedEvent"/> поднимается ⇔ уходим из
    ///     COMPLETED (откат XP / project / module). Вход в COMPLETED через этот метод запрещён — он
    ///     не поднимает <see cref="IssueProgressApprovedEvent"/> (это делает ForceApprove-путь), и
    ///     молчаливый переход в COMPLETED оставил бы XP/project/module несинхронизированными.
    ///     </para>
    ///     <para>Идемпотентно: target == current → no-op (событие не дублируется).</para>
    /// </summary>
    public UnitResult<Error> SetStatusByStaff(IssueProgressStatus target)
    {
        if (target == IssueProgressStatus.COMPLETED)
        {
            // COMPLETED идёт через synthetic-submission ForceApprove-путь в use-case'е — этот метод
            // для него не предназначен (иначе XP/project/module не начислятся).
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueProgress),
                Status.ToString(),
                nameof(SetStatusByStaff));
        }

        if (target == IssueProgressStatus.NOT_STARTED)
        {
            return Reset();
        }

        if (Status == target)
        {
            return UnitResult.Success<Error>();
        }

        bool wasCompleted = Status == IssueProgressStatus.COMPLETED;

        Status = target;
        StartedAt ??= DateTime.UtcNow;
        CompletedAt = null;

        if (wasCompleted)
        {
            RaiseDomainEvent(new IssueProgressReopenedEvent(this));
        }

        return UnitResult.Success<Error>();
    }
}
