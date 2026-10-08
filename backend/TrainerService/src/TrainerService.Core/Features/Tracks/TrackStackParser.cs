using TrainerService.Domain;

namespace TrainerService.Core.Features.Tracks;

/// <summary>Парсит строковый stack из запроса в <see cref="TrackStack"/> (строгий, UPPER_SNAKE).</summary>
internal static class TrackStackParser
{
    public static bool TryParse(string? raw, out TrackStack stack)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            stack = default;
            return false;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: false, out stack) && Enum.IsDefined(stack);
    }
}
