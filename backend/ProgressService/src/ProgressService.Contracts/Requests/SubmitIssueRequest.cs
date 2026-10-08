namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запрос на отправку решения задачи на проверку.
/// </summary>
public sealed record SubmitIssueRequest(string? SubmissionUrl = null, string? ContentPayload = null);
