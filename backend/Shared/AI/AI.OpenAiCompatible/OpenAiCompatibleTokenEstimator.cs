using System.Text.Json;
using SharpToken;

namespace Shared.AI.OpenAiCompatible;

internal sealed class OpenAiCompatibleTokenEstimator : IAiTokenEstimator
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public AiTokenEstimate Estimate(AiTokenEstimateRequest request)
    {
        string text = BuildText(request.Request);
        int textTokens = CountTextTokens(request.Request.Model, text);
        int estimatedTokens = textTokens + EstimateChatOverheadTokens(request.Request) + EstimateAudioTokens(request.Request);

        return new AiTokenEstimate(estimatedTokens, AiTokenEstimateConfidence.High);
    }

    private static string BuildText(AiGenerationRequest request)
    {
        var parts = new List<string> { request.SystemPrompt };

        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
            parts.Add(request.UserPrompt);

        if (request.Input is not null)
            parts.Add(JsonSerializer.Serialize(request.Input, _jsonOptions));

        parts.AddRange(request.InputParts
            .Where(static part => !string.IsNullOrWhiteSpace(part.Text))
            .Select(static part => $"{part.Name}: {part.Text}"));

        return string.Join("\n\n", parts);
    }

    private static int EstimateAudioTokens(AiGenerationRequest request)
    {
        int tokens = 0;

        foreach (AiInputPart inputPart in request.InputParts.Where(static part => part.Type == AiInputPartType.Audio))
        {
            string contentType = inputPart.ContentType ?? string.Empty;
            double estimatedSeconds =
                contentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("mp3", StringComparison.OrdinalIgnoreCase)
                    ? inputPart.Content.Length / 16_000d
                    : inputPart.Content.Length / 96_000d;

            tokens += (int)Math.Ceiling(estimatedSeconds * 32);
        }

        return tokens;
    }

    private static int CountTextTokens(string model, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        try
        {
            GptEncoding encoding = ResolveEncoding(model);
            return encoding.CountTokens(text, null!, null!);
        }
        catch
        {
            return (int)Math.Ceiling(text.Length / 3.5d);
        }
    }

    private static GptEncoding ResolveEncoding(string model)
    {
        try
        {
            return GptEncoding.GetEncodingForModel(model);
        }
        catch
        {
            return GptEncoding.GetEncoding("cl100k_base");
        }
    }

    private static int EstimateChatOverheadTokens(AiGenerationRequest request)
    {
        int messageCount = 1;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messageCount++;

        return 12 + messageCount * 4 + request.InputParts.Count * 8;
    }
}
