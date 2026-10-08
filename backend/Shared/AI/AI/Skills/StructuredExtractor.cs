using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.Skills;

public sealed class StructuredExtractor<TPayload> : IStructuredExtractor<TPayload>
{
    private readonly IAiClient _aiClient;

    public StructuredExtractor(IAiClient aiClient)
    {
        _aiClient = aiClient;
    }

    public async Task<Result<AiStructured<TPayload>, Error>> ExtractAsync(
        StructuredExtractRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserText))
            return AiErrors.InputRequired();

        AiGenerationRequest aiRequest = new()
        {
            Model = request.Model,
            SystemPrompt = request.SystemPrompt,
            UserPrompt = request.UserText,
            Temperature = request.Temperature ?? 0.0,
            MaxOutputTokens = request.MaxOutputTokens,
            TimeoutSeconds = request.TimeoutSeconds,
            OutputMode = AiOutputMode.JsonSchema,
            JsonSchema = request.Schema,
        };

        Result<AiGenerationResult<TPayload>, Error> result =
            await _aiClient.GenerateAsync<TPayload>(aiRequest, cancellationToken);

        if (result.IsFailure)
            return result.Error;

        AiGenerationResult<TPayload> ok = result.Value;
        return new AiStructured<TPayload>(ok.Value!, ok.Usage, ok.FinishReason);
    }
}
