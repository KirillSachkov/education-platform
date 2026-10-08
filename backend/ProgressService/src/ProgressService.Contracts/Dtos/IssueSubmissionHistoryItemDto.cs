namespace ProgressService.Contracts.Dtos;

public sealed record IssueSubmissionHistoryItemDto(
    Guid SubmissionId,
    int AttemptNumber,
    string Payload,
    string ReviewStatus,
    DateTime SubmittedAt,
    DateTime? ReviewStartedAt,
    DateTime? ReviewedAt,
    string? Feedback,
    // #369: AI-денорм поля на каждой попытке — чтобы в раскрытой истории группы
    // у любой попытки можно было показать вердикт и раскрыть её AI-ревью.
    string? LatestAiVerdict,
    int AiIterationsCount,
    DateTime? LastAiIterationAt,
    string? AiReviewStatus,
    // #383: timestamp когда студент позвал автора (null = не звал) — питает
    // студент-side состояние «Автор позван» на странице задачи.
    DateTime? AuthorHelpRequestedAt);
