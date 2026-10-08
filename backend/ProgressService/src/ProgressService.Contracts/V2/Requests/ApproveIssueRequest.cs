namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на одобрение задачи.
/// </summary>
public sealed record ApproveIssueRequest(string? Feedback = null);
