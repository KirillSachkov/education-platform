namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запрос на одобрение задачи.
/// </summary>
public sealed record ApproveIssueRequest(string? Feedback = null);
