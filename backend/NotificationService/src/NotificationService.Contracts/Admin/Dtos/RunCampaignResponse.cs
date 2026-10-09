namespace NotificationService.Contracts.Admin.Dtos;

/// <summary>
/// Результат запуска кампании уведомлений.
/// </summary>
/// <param name="Queued">Скольким пользователям поставлено в очередь уведомление в этом проходе.</param>
public sealed record RunCampaignResponse(int Queued);

/// <summary>
/// Результат тестовой отправки уведомления самому администратору.
/// </summary>
/// <param name="Sent">Признак успешной постановки тестового уведомления в очередь.</param>
public sealed record SendTestCampaignResponse(bool Sent);

/// <summary>
/// Размер адресуемой аудитории кампании уведомлений.
/// </summary>
/// <param name="Count">Число пользователей-адресатов (до opt-out/канальной фильтрации на доставке).</param>
public sealed record CampaignRecipientCountResponse(int Count);