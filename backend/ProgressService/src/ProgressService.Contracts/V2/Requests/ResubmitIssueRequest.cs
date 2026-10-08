namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на повторную отправку задачи.
/// </summary>
public sealed record ResubmitIssueRequest(string ContentPayload);
