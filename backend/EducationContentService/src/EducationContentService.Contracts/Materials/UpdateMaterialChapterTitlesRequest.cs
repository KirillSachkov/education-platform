namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Денормализует главы видео (заголовок + offset) во все материалы, привязанные
///     к указанному videoId. ECS делает fan-out: SELECT materials WHERE video_id = X →
///     UpdateChapters на каждом → publishes material.updated.
/// </summary>
public sealed record UpdateVideoChaptersRequest(
    Guid AssetVersion,
    IReadOnlyList<VideoChapterDto> Chapters);

public sealed record VideoChapterDto(string Title, int TimeSeconds);
