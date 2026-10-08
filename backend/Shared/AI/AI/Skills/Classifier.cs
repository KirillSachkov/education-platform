using System.Text;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

public sealed class Classifier<TLabel> : IClassifier<TLabel> where TLabel : struct, Enum
{
    private readonly IAiClient _aiClient;

    public Classifier(IAiClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<Result<AiClassification<TLabel>, Error>> ClassifyAsync(
        ClassifyRequest<TLabel> request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return AiErrors.InputRequired();

        string[] labels = Enum.GetNames<TLabel>();
        AiJsonSchema schema = BuildSchema(labels);

        AiGenerationRequest aiRequest = new()
        {
            Model = request.Model,
            SystemPrompt = BuildSystemPrompt(request, labels),
            UserPrompt = request.Text,
            Temperature = 0.0,
            TimeoutSeconds = request.TimeoutSeconds,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = schema,
        };

        Result<AiGenerationResult<ClassificationPayload>, Error> result =
            await _aiClient.GenerateAsync<ClassificationPayload>(aiRequest, cancellationToken);

        if (result.IsFailure)
            return result.Error;

        AiGenerationResult<ClassificationPayload> ok = result.Value;
        ClassificationPayload payload = ok.Value ?? new ClassificationPayload();

        if (!Enum.TryParse(payload.Label, ignoreCase: true, out TLabel parsed))
        {
            return Error.Validation(
                "ai.classifier.label_invalid",
                $"Модель вернула неизвестную метку: {payload.Label}");
        }

        return new AiClassification<TLabel>(
            parsed,
            Math.Clamp(payload.Confidence, 0.0, 1.0),
            payload.Rationale,
            ok.Usage,
            ok.FinishReason);
    }

    private static string BuildSystemPrompt(ClassifyRequest<TLabel> request, string[] labels)
    {
        StringBuilder sb = new();
        sb.AppendLine("Ты — классификатор. Прочитай текст и верни одну из меток.");
        sb.Append("Задача: ").Append(request.TaskDescription).AppendLine();
        sb.AppendLine("Допустимые метки:");
        foreach (string label in labels)
        {
            sb.Append("- ").Append(label);
            if (request.LabelDescriptions is { } map &&
                Enum.TryParse(label, out TLabel value) &&
                map.TryGetValue(value, out string? desc) &&
                !string.IsNullOrWhiteSpace(desc))
            {
                sb.Append(" — ").Append(desc);
            }
            sb.AppendLine();
        }
        sb.AppendLine("Формат ответа: JSON {label, confidence (0..1), rationale}.");
        sb.Append("Если уверенности нет — верни наиболее вероятную метку с низким confidence.");
        return sb.ToString();
    }

    private static AiJsonSchema BuildSchema(string[] labels)
    {
        // Compact JSON Schema with enum constraint on label.
        string enumValues = string.Join(",", labels.Select(static l => $"\"{l}\""));
        string schemaJson = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["label", "confidence", "rationale"],
          "properties": {
            "label": { "type": "string", "enum": [{{enumValues}}] },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
            "rationale": { "type": ["string", "null"] }
          }
        }
        """;
        return new AiJsonSchema("Classification", schemaJson, "Классификация текста по одной метке");
    }

    private sealed class ClassificationPayload
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("rationale")]
        public string? Rationale { get; set; }
    }

}
