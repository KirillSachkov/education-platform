using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

/// <summary>
/// Поднимается, когда submission, ранее закрытая для автора AI-гейтом
/// (<see cref="IssueSubmission.GateForAiReview"/>), снова открывается на ручное
/// ревью автору через <see cref="IssueSubmission.Finalize"/> — т.е. AI не справилась
/// (пустой verdict → авто-Finalize в gate) ИЛИ студент явно нажал «Отправить автору».
///
/// Handler публикует integration event <c>IssueSubmissionAwaitingReview</c>, чтобы
/// автор получил свежее уведомление: при гейте submission пропадала из inbox'а и
/// исходное create-time уведомление успевало устареть. Без этого автор не узнаёт,
/// что задача вернулась к нему (notification GAP #334).
/// </summary>
public sealed record IssueSubmissionAwaitingManualReviewEvent(IssueSubmission Submission) : IDomainEvent;
