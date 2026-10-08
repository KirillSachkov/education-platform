namespace FileService.Contracts.Assets;

public sealed record UpdateVideoChaptersRequest(
    Guid GenerationJobId,
    Guid AssetVersion,
    IReadOnlyList<UpdateVideoChapterItemDto> Chapters);

public sealed record UpdateVideoChapterItemDto(
    string Title,
    int StartSeconds,
    int SortOrder);
