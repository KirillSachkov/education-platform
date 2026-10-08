namespace NotificationService.Contracts.Admin.Dtos;

/// <summary>
/// Результат запуска кампании-рассылки приглашений на тест уровня (#554).
/// </summary>
/// <param name="Queued">Скольким пользователям поставлено в очередь приглашение в этом проходе.</param>
public sealed record RunCampaignResponse(int Queued);

/// <summary>
/// Результат тестовой отправки приглашения на тест уровня самому админу (#554).
/// </summary>
/// <param name="Sent">Признак успешной постановки тестового приглашения в очередь.</param>
public sealed record SendTestCampaignResponse(bool Sent);

/// <summary>
/// Размер адресуемой аудитории кампании приглашений на тест уровня (#554).
/// </summary>
/// <param name="Count">Число пользователей-адресатов (до opt-out/канальной фильтрации на доставке).</param>
public sealed record CampaignRecipientCountResponse(int Count);
