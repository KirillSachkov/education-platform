namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на начало работы над задачей.
/// </summary>
public sealed record StartIssueRequest(bool IsRequired);
