namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запрос на доработку задачи. Feedback опционален: ревьюер может вернуть на доработку
///     без комментария (например, обсудит вживую).
/// </summary>
public sealed record RequestIssueChangesRequest(string? Feedback = null);
