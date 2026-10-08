using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.IssueSubmissions;

/// <summary>
/// Запись конкретной отправки решения задачи на проверку.
/// Хранит отправленные данные, состояние проверки, информацию о ревьюере и feedback.
/// </summary>
public sealed class IssueSubmission : AggregateRoot
{
    public const int AUTHOR_HELP_MESSAGE_MAX_LENGTH = 2000;

    private IssueSubmission(
        Guid issueProgressId,
        AttemptNumber attemptNumber,
        IssueSubmissionPayload payload,
        bool autoFinalize)
    {
        Id = Guid.CreateVersion7();
        IssueProgressId = issueProgressId;
        AttemptNumber = attemptNumber;
        Payload = payload;
        ReviewStatus = IssueSubmissionReviewStatus.PENDING;
        SubmittedAt = DateTime.UtcNow;
        ReadyForHumanReview = autoFinalize;
        AiIterationsCount = 0;
    }

    private IssueSubmission()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid IssueProgressId { get; private set; }

    public AttemptNumber AttemptNumber { get; private set; } = null!;

    public IssueSubmissionPayload Payload { get; private set; } = null!;

    public IssueSubmissionReviewStatus ReviewStatus { get; private set; }

    public Guid? ReviewerId { get; private set; }

    public DateTime? ReviewStartedAt { get; private set; }

    public IssueReviewFeedback? Feedback { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    /// <summary>
    ///     Денорм verdict последнего AI iteration'а (Phase 8 #15). Заполняется
    ///     <c>AiReviewIterationCompletedHandler</c> на event из AssignmentReviewService.
    ///     null = AI ещё не запускался (или нет AiReview).
    ///     Возможные значения: "LOOKS_GOOD" / "MINOR_ISSUES" / "MAJOR_ISSUES" / "OFF_TOPIC".
    /// </summary>
    public string? LatestAiVerdict { get; private set; }

    /// <summary>Количество AI iteration'ов, запущенных по этой submission. Default 0.</summary>
    public int AiIterationsCount { get; private set; }

    /// <summary>Timestamp последнего AI iteration'а (success или failure). Phase 8 #15.</summary>
    public DateTime? LastAiIterationAt { get; private set; }

    /// <summary>
    ///     Денорм статус AI-проверки: QUEUED / RUNNING / READY / FAILED. null = нет AiReview.
    /// </summary>
    public string? AiReviewStatus { get; private set; }

    /// <summary>
    ///     Гейт автор-ревью-инбокса (Phase 8 #15). Author видит submission в инбоксе
    ///     только когда true. По-умолчанию true (backward-compat для submissions без
    ///     AI). ARS consumer переводит в false, когда AiReview создан под этой
    ///     submission. Студент возвращает в true через <see cref="Finalize"/> после
    ///     прогона AI iteration'ов.
    /// </summary>
    public bool ReadyForHumanReview { get; private set; }

    /// <summary>
    ///     Timestamp когда студент нажал «Позвать автора» (#383). null = автора ещё не звали.
    ///     По умолчанию автор вне цикла AI-проверки; студент явно подключает его этим действием.
    ///     Выставляется один раз — повторный вызов <see cref="RequestAuthorHelp"/> идемпотентен.
    /// </summary>
    public DateTime? AuthorHelpRequestedAt { get; private set; }

    /// <summary>
    ///     Свободный текст «в чём нужна помощь», опционально указанный студентом при
    ///     «Позвать автора» (#575). Сохраняется один раз вместе с <see cref="AuthorHelpRequestedAt"/>.
    /// </summary>
    public string? AuthorHelpMessage { get; private set; }

    /// <summary>
    ///     Денорм-timestamp последнего вопроса, который студент задал в своём GitHub-PR (#713).
    ///     null = вопросов ещё не было. Заполняется <c>StudentPrQuestionAskedHandler</c> на
    ///     integration event <c>StudentPrQuestionAsked</c> из AssignmentReviewService; питает
    ///     бейдж «новый вопрос от студента» на карточке сдачи в панели «Проверка работ».
    /// </summary>
    public DateTime? StudentQuestionAt { get; private set; }

    /// <summary>
    ///     Создаёт попытку сдачи.
    ///     <para>
    ///     <paramref name="raiseAwaitingReview"/> = <c>true</c> (дефолт) поднимает
    ///     <see cref="IssueSubmissionCreatedEvent"/> → публикуется <c>issue_submission.awaiting_review</c>
    ///     (AI-проверка + уведомление автору «студент сдал»). Передаётся <c>false</c> только для
    ///     синтетического submission'а ручной приёмки автором/админом студенту, который не сдавал
    ///     работу (#398): попытка сразу переводится в APPROVED, поэтому awaiting-review-сигнал не нужен
    ///     и был бы вреден (ложная AI-проверка + ложное уведомление).
    ///     </para>
    /// </summary>
    public static Result<IssueSubmission, Error> Create(
        Guid issueProgressId,
        AttemptNumber attemptNumber,
        IssueSubmissionPayload payload,
        bool autoFinalize = true,
        bool raiseAwaitingReview = true)
    {
        if (issueProgressId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(issueProgressId));
        }

        if (attemptNumber is null)
        {
            return GeneralErrors.ValueIsInvalid(nameof(attemptNumber));
        }

        IssueSubmission submission = new(issueProgressId, attemptNumber, payload, autoFinalize);

        if (raiseAwaitingReview)
        {
            submission.RaiseDomainEvent(new IssueSubmissionCreatedEvent(submission));
        }

        return submission;
    }

    public UnitResult<Error> StartReview(Guid reviewerId)
    {
        if (reviewerId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(reviewerId));
        }

        if (ReviewStatus != IssueSubmissionReviewStatus.PENDING)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(StartReview));
        }

        ReviewerId = reviewerId;
        ReviewStartedAt = DateTime.UtcNow;
        ReviewStatus = IssueSubmissionReviewStatus.IN_REVIEW;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Ревьюер «отказывается от проверки»: возвращает взятую в работу попытку (IN_REVIEW)
    ///     обратно в PENDING («Ожидает проверки»), снимая себя как reviewer'а. Применяется, когда
    ///     автор нажал «Начать ревью», но решил не проверять (передумал / передаёт коллеге).
    ///     Инверсия <see cref="StartReview"/> — IssueProgress не трогается (StartReview его тоже не
    ///     трогал), domain-event не нужен. Снимает AI-гейт (<see cref="ReadyForHumanReview"/> = true),
    ///     чтобы попытка снова была видна во вкладке «Ожидают проверки». Только из IN_REVIEW. (#668)
    /// </summary>
    public UnitResult<Error> CancelReview()
    {
        if (ReviewStatus != IssueSubmissionReviewStatus.IN_REVIEW)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(CancelReview));
        }

        ReviewStatus = IssueSubmissionReviewStatus.PENDING;
        ReviewerId = null;
        ReviewStartedAt = null;
        ReadyForHumanReview = true;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RequestChanges(IssueReviewFeedback? feedback = null)
    {
        if (ReviewStatus != IssueSubmissionReviewStatus.IN_REVIEW)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(RequestChanges));
        }

        Feedback = feedback;
        ReviewStatus = IssueSubmissionReviewStatus.CHANGES_REQUESTED;
        ReviewedAt = DateTime.UtcNow;

        RaiseDomainEvent(new IssueSubmissionChangesRequestedEvent(this));

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Approve(IssueReviewFeedback? feedback = null)
    {
        if (ReviewStatus != IssueSubmissionReviewStatus.IN_REVIEW)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(Approve));
        }

        Feedback = feedback;
        ReviewStatus = IssueSubmissionReviewStatus.APPROVED;
        ReviewedAt = DateTime.UtcNow;

        RaiseDomainEvent(new IssueSubmissionApproveEvent(this));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Ручная приёмка автором/админом («Отметить выполненным», #383): форс-аппрув попытки
    ///     независимо от текущего статуса проверки. AI — ассистент, поэтому автор должен иметь
    ///     возможность принять работу одной кнопкой из любого состояния (PENDING без ревью,
    ///     IN_REVIEW, или CHANGES_REQUESTED — передумал). Нормализует <see cref="ReviewStatus"/>
    ///     в IN_REVIEW (закрепляя <paramref name="reviewerId"/> как принявшего), затем APPROVED.
    ///     <para>
    ///     <paramref name="cascadeProgress"/> = <c>true</c> (дефолт) поднимает
    ///     <see cref="IssueSubmissionApproveEvent"/> — каскад на IssueProgress + XP/project/module +
    ///     integration event <c>issue_submission.approved</c> отрабатывают как у обычного Approve.
    ///     Передаётся <c>false</c>, когда shared <see cref="Issues.IssueProgress"/> уже COMPLETED
    ///     (задачу приняли по другой попытке): XP/project/module начислены, повторный
    ///     <c>IssueProgress.Approve()</c> упал бы на не-UNDER_REVIEW — поэтому закрываем только саму
    ///     попытку, событие не поднимаем.
    ///     </para>
    ///     <para>Идемпотентно: уже APPROVED → no-op success (не дублирует event).</para>
    /// </summary>
    public UnitResult<Error> ForceApprove(
        Guid reviewerId,
        IssueReviewFeedback? feedback = null,
        bool cascadeProgress = true)
    {
        if (reviewerId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(reviewerId));
        }

        if (ReviewStatus == IssueSubmissionReviewStatus.APPROVED)
        {
            return UnitResult.Success<Error>();
        }

        ReviewerId = reviewerId;
        ReviewStartedAt ??= DateTime.UtcNow;
        Feedback = feedback;
        ReviewStatus = IssueSubmissionReviewStatus.APPROVED;
        ReviewedAt = DateTime.UtcNow;

        if (cascadeProgress)
        {
            RaiseDomainEvent(new IssueSubmissionApproveEvent(this));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Студент явно подтверждает что готов отдать submission на ручное ревью
    ///     автору (Phase 8 #15). Идемпотентно. Доступно только в PENDING/IN_REVIEW
    ///     (после approve/changes_requested ничего финализировать не нужно).
    /// </summary>
    public UnitResult<Error> Finalize()
    {
        if (ReviewStatus is not (IssueSubmissionReviewStatus.PENDING or IssueSubmissionReviewStatus.IN_REVIEW))
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(Finalize));
        }

        // Свежее уведомление автору поднимаем только на реальном переходе
        // (submission была закрыта AI-гейтом). Идемпотентный повтор Finalize или
        // финализация submission'а, который и так был открыт (не AI-flow), автора
        // не пингуют — он уже получил create-time уведомление (notification GAP #334).
        bool wasGatedForAi = !ReadyForHumanReview;

        ReadyForHumanReview = true;

        if (wasGatedForAi)
        {
            RaiseDomainEvent(new IssueSubmissionAwaitingManualReviewEvent(this));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Студент нажимает «Позвать автора» (#383) — просит автора курса подключиться к
    ///     проверке (AI-ассистент не справился / нужна живая помощь). Идемпотентно: если
    ///     автора уже звали (<see cref="AuthorHelpRequestedAt"/> выставлен) — no-op success.
    ///     Возвращает <c>true</c> в out-параметре, только если это первый вызов (реальный
    ///     переход), чтобы caller опубликовал integration event ровно один раз.
    ///     <paramref name="message"/> (#575) — опциональный текст «в чём нужна помощь»;
    ///     сохраняется (trimmed, пустой → null) ровно на первом переходе.
    /// </summary>
    public UnitResult<Error> RequestAuthorHelp(DateTime nowUtc, string? message, out bool firstRequest)
    {
        if (AuthorHelpRequestedAt is not null)
        {
            firstRequest = false;
            return UnitResult.Success<Error>();
        }

        AuthorHelpRequestedAt = nowUtc;
        AuthorHelpMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        firstRequest = true;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Денорм-апдейт от <c>StudentPrQuestionAsked</c> event'а (#713): студент задал вопрос
    ///     в своём GitHub-PR. Держит <see cref="StudentQuestionAt"/> на времени последнего вопроса.
    ///     Идемпотентно к ре-доставке (та же метка → no-op) и устойчиво к out-of-order доставке
    ///     (двигается только вперёд), поэтому бейдж всегда отражает самый свежий вопрос.
    /// </summary>
    public void RecordStudentQuestion(DateTime askedAtUtc)
    {
        if (StudentQuestionAt is null || askedAtUtc > StudentQuestionAt.Value)
        {
            StudentQuestionAt = askedAtUtc;
        }
    }

    /// <summary>
    ///     ARS publish'ит <c>AiReviewQueuedForSubmission</c> когда создаёт AiReview под
    ///     эту submission. Гейт: до завершения хотя бы одного iteration'а submission
    ///     не показывается автору в inbox'е. Студент возвращает гейт в open через
    ///     <see cref="Finalize"/>.
    /// </summary>
    public void GateForAiReview()
    {
        ReadyForHumanReview = false;
    }

    /// <summary>
    ///     Помечает submission как «AI-проверка поставлена в очередь» (Phase 8 #15).
    ///     В отличие от <see cref="ApplyAiIteration"/> НЕ трогает счётчик итераций и
    ///     timestamp последнего завершённого iteration'а — никакой iteration ещё не прошёл.
    ///     Идемпотентно: повторная установка статуса QUEUED не сбрасывает уже накопившиеся
    ///     <see cref="AiIterationsCount"/> / <see cref="LastAiIterationAt"/>, если они
    ///     успели обновиться через late-delivered iteration-completed event.
    /// </summary>
    public void MarkAiQueued()
    {
        AiReviewStatus = "QUEUED";
    }

    /// <summary>
    ///     Денорм-апдейт от <c>AiReviewIterationCompleted</c> event'а (Phase 8 #15).
    ///     Не меняет <see cref="ReadyForHumanReview"/> — это исключительно поле
    ///     студент-эджилитики (через Finalize).
    /// </summary>
    public void ApplyAiIteration(
        string? verdict,
        int iterationsCount,
        DateTime completedAt,
        string aiReviewStatus)
    {
        LatestAiVerdict = string.IsNullOrEmpty(verdict) ? null : verdict;
        AiIterationsCount = iterationsCount;
        LastAiIterationAt = completedAt;
        AiReviewStatus = aiReviewStatus;
    }

    /// <summary>
    ///     Возвращает уже проверенную попытку (APPROVED или CHANGES_REQUESTED) обратно в статус
    ///     IN_REVIEW, очищая feedback и ReviewedAt. Используется, когда ревьюер хочет пересмотреть
    ///     своё решение (например, поставил Approve по ошибке). Каскад на IssueProgress + откат
    ///     XP / project / module прогресса делается через домен-event <see cref="IssueSubmissionReviewReopenedEvent"/>.
    /// </summary>
    public UnitResult<Error> ReopenReview()
    {
        if (ReviewStatus != IssueSubmissionReviewStatus.APPROVED
            && ReviewStatus != IssueSubmissionReviewStatus.CHANGES_REQUESTED)
        {
            return ProgressErrors.InvalidStatusTransition(
                nameof(IssueSubmission),
                ReviewStatus.ToString(),
                nameof(ReopenReview));
        }

        bool wasApproved = ReviewStatus == IssueSubmissionReviewStatus.APPROVED;

        ReviewStatus = IssueSubmissionReviewStatus.IN_REVIEW;
        Feedback = null;
        ReviewedAt = null;

        // #454: reopen возвращает работу в человеческую очередь ревью. Pending-инбокс
        // (GetPendingReviewIssues) показывает IN_REVIEW только при ready_for_human_review=true.
        // Если попытка была AI-авто-проверена (GateForAiReview оставил флаг false, а Approve/
        // RequestChanges его не возвращают), без снятия гейта reopened-сабмишн выпадал из ВСЕХ
        // трёх listing-фильтров (pending/in-review/reviewed) и пропадал из всех вкладок.
        ReadyForHumanReview = true;

        RaiseDomainEvent(new IssueSubmissionReviewReopenedEvent(this, wasApproved));

        return UnitResult.Success<Error>();
    }
}
