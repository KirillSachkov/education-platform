namespace ProgressService.Contracts.Responses;

/// <summary>
///     Ответ на отправку решения задачи.
/// </summary>
public sealed record SubmitIssueResponse(Guid SubmissionId);
