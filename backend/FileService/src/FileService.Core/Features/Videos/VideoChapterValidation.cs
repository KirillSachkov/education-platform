using FileService.Contracts.Assets;

namespace FileService.Core.Features.Videos;

internal static class VideoChapterValidation
{
    public static Result<IReadOnlyList<VideoChapterDefinition>, Error> ValidateManual(
        IReadOnlyList<ReplaceVideoChapterItemDto> chapters)
        => ValidateCore(chapters.Select(x => (x.Title, x.StartSeconds)));

    public static Result<IReadOnlyList<VideoChapterDefinition>, Error> ValidateGenerated(
        IReadOnlyList<UpdateVideoChapterItemDto> chapters)
        => ValidateCore(chapters.Select(x => (x.Title, (double)x.StartSeconds)));

    private static Result<IReadOnlyList<VideoChapterDefinition>, Error> ValidateCore(
        IEnumerable<(string Title, double StartSeconds)> chapters)
    {
        List<VideoChapterDefinition> normalized = chapters
            .Select(x => new
            {
                Title = x.Title.Trim(),
                x.StartSeconds,
            })
            .OrderBy(x => x.StartSeconds)
            .Select(x => new VideoChapterDefinition(x.StartSeconds, x.Title))
            .ToList();

        for (int index = 0; index < normalized.Count; index++)
        {
            VideoChapterDefinition current = normalized[index];

            if (string.IsNullOrWhiteSpace(current.Title))
            {
                return Error.Validation(
                    "video.chapters.title.required",
                    "У каждой главы должен быть заголовок");
            }

            if (current.StartSeconds < 0)
            {
                return Error.Validation(
                    "video.chapters.start.invalid",
                    "Время начала главы не может быть отрицательным");
            }

            if (index == 0)
                continue;

            if (current.StartSeconds <= normalized[index - 1].StartSeconds)
            {
                return Error.Validation(
                    "video.chapters.order.invalid",
                    "Главы должны идти по возрастанию времени");
            }
        }

        return normalized.ToArray();
    }
}
