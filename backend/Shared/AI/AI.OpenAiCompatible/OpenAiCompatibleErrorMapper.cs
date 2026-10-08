using System.ClientModel;
using System.Net;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal static class OpenAiCompatibleErrorMapper
{
    public static Error Map(Exception exception)
    {
        if (exception is ClientResultException clientException)
            return MapClientResultException(clientException);

        string message = exception.ToString();
        return MapByMessage(message);
    }

    private static Error MapClientResultException(ClientResultException exception)
    {
        string message = exception.Message + "\n" + TryGetResponseContent(exception);

        if (ContainsContextExceeded(message))
            return AiErrors.ContextExceeded();

        if (ContainsSchemaError(message))
            return AiErrors.JsonSchemaInvalid();

        return exception.Status switch
        {
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden =>
                AiErrors.ProviderUnauthorized(),

            (int)HttpStatusCode.NotFound =>
                AiErrors.ModelUnavailable(),

            (int)HttpStatusCode.RequestTimeout =>
                AiErrors.ProviderTimeout(),

            (int)HttpStatusCode.TooManyRequests =>
                AiErrors.ProviderRateLimited(),

            (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.UnprocessableEntity
                when ContainsModelUnavailable(message) =>
                AiErrors.ModelUnavailable(),

            _ => MapByMessage(message),
        };
    }

    private static Error MapByMessage(string message)
    {
        if (ContainsContextExceeded(message))
            return AiErrors.ContextExceeded();

        if (ContainsUnauthorized(message))
            return AiErrors.ProviderUnauthorized();

        if (ContainsRateLimit(message))
            return AiErrors.ProviderRateLimited();

        if (ContainsModelUnavailable(message))
            return AiErrors.ModelUnavailable();

        if (ContainsSchemaError(message))
            return AiErrors.JsonSchemaInvalid();

        return AiErrors.ProviderFailed();
    }

    private static string TryGetResponseContent(ClientResultException exception)
    {
        try
        {
            return exception.GetRawResponse()?.Content.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ContainsContextExceeded(string message) =>
        message.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnauthorized(string message) =>
        message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRateLimit(string message) =>
        message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsModelUnavailable(string message) =>
        message.Contains("404", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("model not found", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("model unavailable", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("no endpoints found", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSchemaError(string message) =>
        message.Contains("json schema", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("structured outputs", StringComparison.OrdinalIgnoreCase);
}
