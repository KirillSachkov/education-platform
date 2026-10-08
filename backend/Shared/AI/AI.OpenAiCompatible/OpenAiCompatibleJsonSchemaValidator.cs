using System.Text.Json;
using CSharpFunctionalExtensions;
using Json.Schema;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal static class OpenAiCompatibleJsonSchemaValidator
{
    private static readonly EvaluationOptions _evaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    public static Result<string, Error> Validate(
        string normalizedContent,
        OpenAiCompatibleResolvedRequest resolved)
    {
        if (resolved.OutputMode != AiOutputMode.JsonSchema)
            return normalizedContent;

        if (resolved.JsonSchema is null)
            return AiErrors.JsonSchemaRequired();

        try
        {
            using JsonDocument payloadDocument = JsonDocument.Parse(normalizedContent);
            JsonSchema schema = JsonSchema.FromText(resolved.JsonSchema.Schema);
            EvaluationResults validationResult = schema.Evaluate(
                payloadDocument.RootElement,
                _evaluationOptions);

            return validationResult.IsValid
                ? normalizedContent
                : AiErrors.OutputInvalid();
        }
        catch (JsonException)
        {
            return AiErrors.OutputInvalid();
        }
        catch
        {
            return AiErrors.JsonSchemaInvalid();
        }
    }
}
