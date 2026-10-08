namespace FileService.Contracts.Assets;

public sealed record GetVideoChaptersResponse(
    Guid VideoId,
    IReadOnlyList<VideoChapterDto> Chapters);
