using SharedKernel;

namespace Shared.AI;

public static class AiErrors
{
    public static Error ModelRequired() =>
        Error.Validation("ai.model.required", "Не указана модель для выполнения запроса");

    public static Error ModelMetadataUnavailable() =>
        Error.Validation("ai.model.metadata_unavailable", "Не удалось определить контекст выбранной модели");

    public static Error InputRequired() =>
        Error.Validation("ai.input.required", "Для генерации нужно передать промпт или входные данные");

    public static Error InputUnsupported(string message = "Текущий AI-провайдер не поддерживает такой тип входных данных") =>
        Error.Validation("ai.input.unsupported", message);

    public static Error ContextExceeded() =>
        Error.Validation("ai.context.exceeded", "Запрос не помещается в контекст выбранной модели");

    public static Error OutputEmpty() =>
        Error.Failure("ai.output.empty", "Нейросеть не вернула результат");

    public static Error OutputInvalid() =>
        Error.Failure("ai.output.invalid", "Нейросеть вернула некорректный формат ответа");

    public static Error JsonSchemaRequired() =>
        Error.Validation("ai.output.schema_required", "Для строгого JSON-ответа нужно передать JSON Schema");

    public static Error JsonSchemaUnsupported() =>
        Error.Validation("ai.output.schema_unsupported", "Выбранная модель не поддерживает строгий JSON Schema ответ");

    public static Error ProviderTimeout() =>
        Error.Failure("ai.provider.timeout", "AI-провайдер не успел ответить вовремя");

    public static Error ProviderUnauthorized() =>
        Error.Failure("ai.provider.unauthorized", "AI-провайдер отклонил API-ключ");

    public static Error ProviderRateLimited() =>
        Error.Failure("ai.provider.rate_limited", "AI-провайдер временно ограничил количество запросов");

    public static Error ModelUnavailable() =>
        Error.Validation("ai.model.unavailable", "Выбранная модель недоступна у AI-провайдера");

    public static Error JsonSchemaInvalid() =>
        Error.Validation("ai.output.schema_invalid", "AI-провайдер отклонил JSON Schema");

    public static Error ProviderFailed() =>
        Error.Failure("ai.provider.failed", "Не удалось получить ответ от нейросети");

    public static Error EmbeddingsInputsEmpty() =>
        Error.Validation("ai.embeddings.inputs.empty", "Список входных строк не может быть пустым");

    public static Error EmbeddingsTooManyInputs(int max) =>
        Error.Validation("ai.embeddings.inputs.too_many", $"Максимум {max} входных строк за один запрос");

    public static Error EmbeddingsResponseInvalid() =>
        Error.Failure("ai.embeddings.response.invalid", "Embedding-провайдер вернул некорректный ответ");
}
