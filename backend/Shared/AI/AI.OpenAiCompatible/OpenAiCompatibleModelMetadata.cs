namespace Shared.AI.OpenAiCompatible;

internal sealed record OpenAiCompatibleModelMetadata(
    int? ContextLength,
    HashSet<string> InputModalities,
    HashSet<string> SupportedParameters);
