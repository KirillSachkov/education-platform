namespace NotificationService.Contracts.Admin.Dtos;

/// <summary>
/// Результат ручного прохода еженедельного дайджеста (#468).
/// </summary>
/// <param name="UsersNotified">Скольким пользователям отправлен дайджест в этом проходе.</param>
public sealed record RunDigestResponse(int UsersNotified);
