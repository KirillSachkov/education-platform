namespace AccessService.Contracts.HomePins;

/// <summary>Закрепить материал в плане. Добавляется в конец списка. Epic #397.</summary>
public sealed record AddHomePinRequest(Guid MaterialId, string? Note);

/// <summary>Обновить заметку у закрепа (<c>null</c> очищает).</summary>
public sealed record UpdateHomePinNoteRequest(string? Note);

/// <summary>
///     Переупорядочить закреп — указать <see cref="BeforeId"/> и/или <see cref="AfterId"/>
///     для вычисления новой fractional-позиции (как у onboarding-шагов).
/// </summary>
public sealed record ReorderHomePinRequest(Guid? BeforeId, Guid? AfterId);
