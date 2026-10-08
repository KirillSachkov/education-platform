namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на начало работы над элементом модуля.
/// </summary>
public sealed record StartItemRequest(
    string ItemType,
    bool IsRequired);
