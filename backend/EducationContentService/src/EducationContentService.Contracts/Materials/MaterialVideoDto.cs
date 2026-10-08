namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Метаданные видеозаписи, прикреплённой к материалу (приходит из FileService).
/// </summary>
public sealed record MaterialVideoDto(
    string? ExternalVideoId,
    string? ThumbnailUrl,
    double? DurationSeconds,
    string? Status);
