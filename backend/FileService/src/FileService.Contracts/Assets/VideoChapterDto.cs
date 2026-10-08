namespace FileService.Contracts.Assets;

public sealed record VideoChapterDto(
    string Id,
    string Title,
    double StartSeconds,
    int SortOrder);
