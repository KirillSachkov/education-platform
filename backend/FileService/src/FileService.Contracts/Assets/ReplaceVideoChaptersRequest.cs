namespace FileService.Contracts.Assets;

public sealed record ReplaceVideoChaptersRequest(
    IReadOnlyList<ReplaceVideoChapterItemDto> Chapters);

public sealed record ReplaceVideoChapterItemDto(
    string? Id,
    string Title,
    double StartSeconds,
    int SortOrder);
