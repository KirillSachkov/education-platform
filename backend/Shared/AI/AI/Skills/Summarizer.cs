using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

/// <summary>
///     Default-implementation поверх <see cref="IAiClient"/>. Сервис может зарегистрировать
///     свой собственный <see cref="ISummarizer"/> если нужен кастомный prompt — этот класс
///     даёт baseline.
/// </summary>
public sealed class Summarizer : ISummarizer
{
    private readonly IAiClient _aiClient;

    public Summarizer(IAiClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<Result<AiSummary, Error>> SummarizeAsync(
        SummarizeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return AiErrors.InputRequired();

        string systemPrompt = BuildSystemPrompt(request);

        AiGenerationRequest aiRequest = new()
        {
            Model = request.Model,
            SystemPrompt = systemPrompt,
            UserPrompt = request.Text,
            Temperature = request.Temperature ?? 0.2,
            MaxOutputTokens = request.MaxOutputTokens,
            TimeoutSeconds = request.TimeoutSeconds,
            OutputMode = AiOutputMode.Text,
        };

        Result<AiGenerationResult<string>, Error> result =
            await _aiClient.GenerateAsync<string>(aiRequest, cancellationToken);

        if (result.IsFailure)
            return result.Error;

        AiGenerationResult<string> ok = result.Value;
        return new AiSummary(ok.Value ?? string.Empty, ok.Usage, ok.FinishReason);
    }

    private static string BuildSystemPrompt(SummarizeRequest request)
    {
        string styleHint = request.Style switch
        {
            SummaryStyle.Bullets => "Сформулируй как маркированный список ключевых тезисов.",
            SummaryStyle.Prose => "Напиши связный текст без списков, в 1–3 абзацах.",
            SummaryStyle.Sections => "Сделай структуру с короткими подзаголовками (## Section).",
            _ => "Сделай аккуратный конспект.",
        };

        string lengthHint = request.Length switch
        {
            SummaryLength.Brief => "Будь кратким (~100–200 слов).",
            SummaryLength.Medium => "Объём ~300–600 слов.",
            SummaryLength.Detailed => "Объём ~800–1500 слов.",
            _ => "",
        };

        string langHint = string.IsNullOrWhiteSpace(request.Language)
            ? "Используй язык исходного текста."
            : $"Пиши на языке: {request.Language}.";

        string ctx = string.IsNullOrWhiteSpace(request.Context)
            ? string.Empty
            : $" Дополнительный контекст: {request.Context}.";

        return string.Join(' ',
            "Ты — помощник, который делает аккуратные конспекты.",
            styleHint,
            lengthHint,
            langHint,
            "Сохраняй фактологию, не выдумывай детали.",
            ctx).Trim();
    }
}
