namespace ProgressService.Contracts.V2.Requests;

/// <summary>
///     V2: Запрос на завершение элемента модуля.
/// </summary>
public sealed record CompleteItemRequest(
    string ItemType,
    bool IsRequired);
