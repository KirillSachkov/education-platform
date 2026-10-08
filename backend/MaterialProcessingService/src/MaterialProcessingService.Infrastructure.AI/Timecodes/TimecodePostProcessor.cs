using SharedKernel;
using MaterialProcessingService.Core.Features.Timecodes;

namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal static class TimecodePostProcessor
{
    private const int MAX_GAP_WITHOUT_CHAPTER_SECONDS = 420;
    private const int MAX_CANDIDATES_PER_RESPONSE = 500;
    private const int MAX_WINDOWED_CANDIDATES = 2500;

    public static List<GeneratedVideoTimecode> NormalizeAndMergeFinalTimecodes(
        IReadOnlyList<TimecodeItemResponse> timecodes,
        TimeSpan duration,
        int minGapBetweenTimecodesSeconds)
    {
        GeneratedVideoTimecode[] normalizedTimecodes = NormalizeFinalTimecodes(timecodes, duration);
        return MergeTimecodes(
            normalizedTimecodes,
            (int)Math.Round(duration.TotalSeconds),
            minGapBetweenTimecodesSeconds);
    }

    public static GeneratedVideoTimecode[] NormalizeFinalTimecodes(
        IReadOnlyList<TimecodeItemResponse> timecodes,
        TimeSpan duration)
    {
        return timecodes
            .Take(MAX_CANDIDATES_PER_RESPONSE)
            .Where(item => !IsGenericTitle(item.Title))
            .Where(item => item.StartSeconds >= 0 && item.StartSeconds <= duration.TotalSeconds)
            .Select(item => new GeneratedVideoTimecode(
                item.StartSeconds,
                item.EndSeconds,
                item.Title.Trim(),
                item.Confidence))
            .ToArray();
    }

    public static List<GeneratedVideoTimecode> FinalizeFromWindowedFlow(
        IReadOnlyList<GeneratedVideoTimecode> finalTimecodes,
        IReadOnlyList<WindowTopicProposal> localTopics,
        TimeSpan duration,
        int minGapBetweenTimecodesSeconds)
    {
        int durationSeconds = (int)Math.Round(duration.TotalSeconds);
        List<GeneratedVideoTimecode> coverageAdjustedTimecodes = FillLargeGaps(
            finalTimecodes.Take(MAX_WINDOWED_CANDIDATES).ToArray(),
            localTopics.Take(MAX_WINDOWED_CANDIDATES).ToArray(),
            durationSeconds,
            MAX_GAP_WITHOUT_CHAPTER_SECONDS);

        return MergeTimecodes(
            coverageAdjustedTimecodes,
            durationSeconds,
            minGapBetweenTimecodesSeconds);
    }

    public static WindowTopicProposalItemResponse[] NormalizeWindowTopics(
        IReadOnlyList<WindowTopicProposalItemResponse> timecodes,
        TranscriptWindow window)
    {
        if (timecodes.Count == 0)
            return [];

        int windowDuration = Math.Max(0, window.EndSeconds - window.StartSeconds);

        return timecodes
            .Take(MAX_CANDIDATES_PER_RESPONSE)
            .Select(item => NormalizeWindowTimecode(item, window, windowDuration))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    public static Error MapAiError(Error error)
    {
        string? errorCode = error.Messages.Count > 0
            ? error.Messages[0].Code
            : null;

        return errorCode switch
        {
            "ai.output.empty" => Error.Failure(
                "timecodes.generation.empty",
                "Нейросеть не вернула результат генерации тайм-кодов"),
            "ai.output.invalid" => Error.Failure(
                "timecodes.generation.invalid",
                "Не удалось разобрать ответ генерации тайм-кодов"),
            "ai.context.exceeded" => Error.Validation(
                "timecodes.generation.context_exceeded",
                "Расшифровка не помещается в контекст выбранной модели"),
            _ => Error.Failure(
                "timecodes.generation_failed",
                "Не удалось сгенерировать тайм-коды")
        };
    }

    public static bool IsSkippableWindowError(Error error)
    {
        return error.Messages.Any(message =>
            message.Code is "timecodes.generation.empty" or "timecodes.generation.invalid");
    }

    public static bool IsGenericTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        string normalizedTitle = NormalizeTitle(title);

        return normalizedTitle is
            "введение" or
            "описание проблемы" or
            "поиск решения" or
            "поиск решения проблемы" or
            "реализация решения" or
            "тестирование и отладка" or
            "выводы" or
            "выводы и результаты" or
            "обсуждение вопросов" or
            "введение в модуль" or
            "проектирование функциональности" or
            "реализация функциональности" or
            "завершение разработки";
    }

    public static IReadOnlyList<GeneratedVideoTimecode> ConvertTopicsToTimecodes(
        IReadOnlyList<WindowTopicProposal> topics)
    {
        return topics
            .Where(topic => !IsGenericTitle(topic.Title))
            .OrderBy(topic => topic.StartSeconds)
            .Select(topic => new GeneratedVideoTimecode(
                topic.StartSeconds,
                topic.EndSeconds,
                topic.Title,
                topic.Confidence))
            .ToArray();
    }

    private static WindowTopicProposalItemResponse? NormalizeWindowTimecode(
        WindowTopicProposalItemResponse item,
        TranscriptWindow window,
        int windowDuration)
    {
        int normalizedStartSeconds;
        int? normalizedEndSeconds;

        if (item.StartSeconds >= window.StartSeconds && item.StartSeconds <= window.EndSeconds)
        {
            normalizedStartSeconds = item.StartSeconds;
            normalizedEndSeconds = NormalizeAbsoluteEndSeconds(item.EndSeconds, normalizedStartSeconds, window.EndSeconds);
        }
        else if (item.StartSeconds >= 0 && item.StartSeconds <= windowDuration)
        {
            normalizedStartSeconds = window.StartSeconds + item.StartSeconds;
            normalizedEndSeconds = NormalizeRelativeEndSeconds(item.EndSeconds, normalizedStartSeconds, window, windowDuration);
        }
        else
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.Evidence))
        {
            return null;
        }

        return new WindowTopicProposalItemResponse(
            normalizedStartSeconds,
            normalizedEndSeconds,
            item.Title.Trim(),
            item.Evidence.Trim(),
            item.Confidence);
    }

    private static int? NormalizeAbsoluteEndSeconds(
        int? endSeconds,
        int normalizedStartSeconds,
        int windowEndSeconds)
    {
        if (!endSeconds.HasValue)
            return null;

        int normalizedEndSeconds = Math.Min(endSeconds.Value, windowEndSeconds);
        return normalizedEndSeconds >= normalizedStartSeconds
            ? normalizedEndSeconds
            : null;
    }

    private static int? NormalizeRelativeEndSeconds(
        int? endSeconds,
        int normalizedStartSeconds,
        TranscriptWindow window,
        int windowDuration)
    {
        if (!endSeconds.HasValue || endSeconds.Value < 0)
            return null;

        int candidateEndSeconds = endSeconds.Value <= windowDuration
            ? window.StartSeconds + endSeconds.Value
            : endSeconds.Value;

        int normalizedEndSeconds = Math.Min(candidateEndSeconds, window.EndSeconds);
        return normalizedEndSeconds >= normalizedStartSeconds
            ? normalizedEndSeconds
            : null;
    }

    private static List<GeneratedVideoTimecode> MergeTimecodes(
        IReadOnlyList<GeneratedVideoTimecode> timecodes,
        int durationSeconds,
        int minGapBetweenTimecodesSeconds)
    {
        List<GeneratedVideoTimecode> ordered = timecodes
            .Where(timecode => timecode.StartSeconds >= 0 && timecode.StartSeconds <= durationSeconds)
            .Where(timecode => !IsGenericTitle(timecode.Title))
            .OrderBy(timecode => timecode.StartSeconds)
            .ThenByDescending(timecode => timecode.Confidence)
            .ToList();

        List<GeneratedVideoTimecode> result = [];

        foreach (GeneratedVideoTimecode current in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(current);
                continue;
            }

            GeneratedVideoTimecode previous = result[^1];
            if (current.StartSeconds - previous.StartSeconds < minGapBetweenTimecodesSeconds)
            {
                if (current.Confidence > previous.Confidence)
                    result[^1] = current;

                continue;
            }

            if (string.Equals(NormalizeTitle(previous.Title), NormalizeTitle(current.Title), StringComparison.Ordinal))
            {
                if (current.Confidence > previous.Confidence)
                    result[^1] = current;

                continue;
            }

            result.Add(current);
        }

        return result;
    }

    private static List<GeneratedVideoTimecode> FillLargeGaps(
        IReadOnlyList<GeneratedVideoTimecode> finalTimecodes,
        IReadOnlyList<WindowTopicProposal> localTopics,
        int durationSeconds,
        int maxGapWithoutChapterSeconds)
    {
        List<GeneratedVideoTimecode> combined = finalTimecodes
            .OrderBy(timecode => timecode.StartSeconds)
            .ToList();

        List<WindowTopicProposal> orderedLocalTopics = localTopics
            .Where(topic => !IsGenericTitle(topic.Title))
            .OrderBy(topic => topic.StartSeconds)
            .ToList();

        if (combined.Count == 0)
            return ConvertTopicsToTimecodes(orderedLocalTopics).ToList();

        foreach (WindowTopicProposal localTopic in orderedLocalTopics)
        {
            bool alreadyCovered = combined.Any(existing =>
                Math.Abs(existing.StartSeconds - localTopic.StartSeconds) <= 120);

            if (alreadyCovered)
                continue;

            GeneratedVideoTimecode? previous = combined
                .Where(existing => existing.StartSeconds < localTopic.StartSeconds)
                .OrderByDescending(existing => existing.StartSeconds)
                .FirstOrDefault();

            GeneratedVideoTimecode? next = combined
                .Where(existing => existing.StartSeconds > localTopic.StartSeconds)
                .OrderBy(existing => existing.StartSeconds)
                .FirstOrDefault();

            int gapBefore = previous is null
                ? localTopic.StartSeconds
                : localTopic.StartSeconds - previous.StartSeconds;

            int gapAfter = next is null
                ? durationSeconds - localTopic.StartSeconds
                : next.StartSeconds - localTopic.StartSeconds;

            if (gapBefore < maxGapWithoutChapterSeconds && gapAfter < maxGapWithoutChapterSeconds)
                continue;

            combined.Add(new GeneratedVideoTimecode(
                localTopic.StartSeconds,
                localTopic.EndSeconds,
                localTopic.Title,
                localTopic.Confidence));
        }

        return combined
            .OrderBy(timecode => timecode.StartSeconds)
            .ToList();
    }

    private static string NormalizeTitle(string title)
    {
        return title.Trim().TrimEnd('.', ':', ';', ',', '!', '?').ToLowerInvariant();
    }
}
