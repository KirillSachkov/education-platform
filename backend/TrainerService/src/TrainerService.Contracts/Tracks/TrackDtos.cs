namespace TrainerService.Contracts.Tracks;

/// <summary>Запрос на создание трека тренажёра (admin/seed).</summary>
public sealed record CreateTrackRequest(
    string? Slug,
    string? Title,
    string? Stack,
    string? Description);

/// <summary>Запрос на обновление деталей трека (admin/seed).</summary>
public sealed record UpdateTrackRequest(
    string? Title,
    string? Stack,
    string? Description);

/// <summary>Идентификатор созданного трека.</summary>
public sealed record TrackIdResponse(Guid TrackId);

/// <summary>
///     Трек в студенческом верхнем селекторе хаба: метаданные + число опубликованных тем.
///     Метаданные треков — не gated (как каталог курсов).
/// </summary>
public sealed record TrackDto(
    Guid Id,
    string Slug,
    string Title,
    string Stack,
    string? Description,
    int TopicCount);
