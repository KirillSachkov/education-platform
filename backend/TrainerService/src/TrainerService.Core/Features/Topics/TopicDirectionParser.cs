using TrainerService.Domain;

namespace TrainerService.Core.Features.Topics;

/// <summary>Парсит опциональное строковое direction из запроса в <see cref="TopicDirection"/>?.</summary>
internal static class TopicDirectionParser
{
    public static Result<TopicDirection?, Error> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (TopicDirection?)null;

        if (Enum.TryParse(raw.Trim(), ignoreCase: false, out TopicDirection parsed) && Enum.IsDefined(parsed))
            return parsed;

        return TrainerServiceErrors.Topic.InvalidDirection(raw);
    }
}
