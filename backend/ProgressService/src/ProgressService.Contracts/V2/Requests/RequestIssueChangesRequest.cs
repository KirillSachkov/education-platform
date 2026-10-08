namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на доработку задачи. Feedback опционален.
/// </summary>
public sealed record RequestIssueChangesRequest(string? Feedback = null);
