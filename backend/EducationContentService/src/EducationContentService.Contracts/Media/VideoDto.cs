namespace EducationContentService.Contracts.Media;

/// <summary>
/// DTO с информацией о видео.
/// DTO containing video information.
/// </summary>
public record VideoDto
{
    /// <summary>
    /// Уникальный идентификатор видео.
    /// Unique identifier of the video.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Идентификатор видео во внешнем хранилище (например, Kinescope).
    /// Video ID in external storage (e.g., Kinescope).
    /// </summary>
    public string? VideoId { get; init; }

    /// <summary>
    /// Статус обработки видео (ready, processing, failed, uploading).
    /// Video processing status (ready, processing, failed, uploading).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// URL миниатюры видео.
    /// URL of the video thumbnail.
    /// </summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Длительность видео в секундах.
    /// Duration of the video in seconds.
    /// </summary>
    public double? Duration { get; init; }

    /// <summary>
    /// Флаг принадлежности хранилища (true — собственное хранилище, false — внешнее).
    /// Flag indicating storage ownership (true — own storage, false — external).
    /// </summary>
    public bool IsOwnedStorage { get; init; }
}
