using System.Text.Json;

namespace FileService.Infrastructure.Kinescope.Responses;

internal sealed record KinescopeVideoData(
    string Id,
    string Title,
    string Status,
    KinescopePoster? Poster,
    double? Duration,
    int? Width,
    int? Height,
    JsonElement? Chapters);
