using CSharpFunctionalExtensions;
using OpenAI.Chat;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal static class OpenAiCompatibleChatOptionsFactory
{
    public static Result<ChatCompletionOptions, Error> Build(OpenAiCompatibleResolvedRequest resolved)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = resolved.MaxOutputTokens,
        };

        if (resolved.Temperature.HasValue)
            options.Temperature = (float)resolved.Temperature.Value;

        if (resolved.OutputMode == AiOutputMode.JsonObject && resolved.ModelInfo.SupportsJsonResponseFormat)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
            return options;
        }

        if (resolved.OutputMode != AiOutputMode.JsonSchema)
            return options;

        if (resolved.JsonSchema is null)
            return AiErrors.JsonSchemaRequired();

        if (!resolved.ModelInfo.SupportsJsonSchemaResponseFormat)
        {
            // Graceful degrade: the model can't enforce strict json_schema (e.g.
            // DeepSeek via AITunnel) but supports plain json_object. Fall back so
            // generation still yields valid JSON instead of failing outright. Callers
            // that need a specific shape describe it in the prompt and parse
            // defensively (AiReviewer retries once on a parse mismatch). Only a model
            // that supports neither stays schema-less (provider validates downstream).
            if (resolved.ModelInfo.SupportsJsonResponseFormat)
                options.ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat();
            else
                return AiErrors.JsonSchemaUnsupported();

            return options;
        }

        options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            resolved.JsonSchema.Name,
            BinaryData.FromString(resolved.JsonSchema.Schema),
            resolved.JsonSchema.Description,
            resolved.JsonSchema.Strict);

        return options;
    }
}
