namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на отправку решения задачи.
/// </summary>
public sealed record SubmitIssueRequest(string ContentPayload);
