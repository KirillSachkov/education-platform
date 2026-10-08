using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal sealed class OpenAiCompatibleRequestResolver
{
    private const string PROVIDER_NAME_VALUE = "OpenAiCompatible";
    private const int DEFAULT_RESERVED_OUTPUT_TOKENS = 32_768;
    private const int DEFAULT_TIMEOUT_SECONDS = 300;

    private readonly AiOptions _options;
    private readonly IAiModelCatalog _modelCatalog;
    private readonly IAiTokenEstimator _tokenEstimator;

    public OpenAiCompatibleRequestResolver(
        AiOptions options,
        IAiModelCatalog modelCatalog,
        IAiTokenEstimator tokenEstimator)
    {
        _options = options;
        _modelCatalog = modelCatalog;
        _tokenEstimator = tokenEstimator;
    }

    public async Task<Result<OpenAiCompatibleResolvedRequest, Error>> ResolveAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            return AiErrors.ModelRequired();

        Result<AiModelInfo, Error> modelResult = await _modelCatalog.GetModelAsync(request.Model, cancellationToken);
        if (modelResult.IsFailure)
            return modelResult.Error;

        AiModelInfo modelInfo = modelResult.Value;
        if (modelInfo.ContextWindowTokens <= 0)
            return AiErrors.ModelMetadataUnavailable();

        if (request.InputParts.Any(static part => part.Type == AiInputPartType.File))
            return AiErrors.InputUnsupported();

        if (request.InputParts.Any(static part => part.Type == AiInputPartType.Audio) &&
            !modelInfo.SupportsAudioInput)
        {
            return AiErrors.InputUnsupported("Текущая модель не поддерживает обработку аудио");
        }

        int maxOutputTokens = request.MaxOutputTokens
                              ?? DEFAULT_RESERVED_OUTPUT_TOKENS;

        AiTokenEstimate tokenEstimate = _tokenEstimator.Estimate(
            new AiTokenEstimateRequest(
                request,
                modelInfo.IsMetadataAvailable ? AiTokenEstimateConfidence.Medium : AiTokenEstimateConfidence.Low));

        var analysis = new AiBudgetAnalysis(
            PROVIDER_NAME_VALUE,
            request.Model,
            modelInfo.ContextWindowTokens,
            tokenEstimate.TokenCount,
            maxOutputTokens,
            tokenEstimate.TokenCount + maxOutputTokens <= modelInfo.ContextWindowTokens,
            tokenEstimate.Confidence);

        return new OpenAiCompatibleResolvedRequest(
            request.Model,
            request.Temperature,
            maxOutputTokens,
            request.TimeoutSeconds ?? Math.Max(1, _options.TimeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS),
            request.OutputMode ?? AiOutputMode.JsonObject,
            request.JsonSchema,
            modelInfo,
            analysis);
    }
}
