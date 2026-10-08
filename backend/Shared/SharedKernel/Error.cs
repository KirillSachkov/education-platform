using System.Text.Json.Serialization;
using SharedKernel.Exceptions;

namespace SharedKernel;

public record ErrorMessage(string Code, string Message, string? InvalidField = null);

public record Error
{
    public IReadOnlyList<ErrorMessage> Messages { get; } = [];

    public ErrorType Type { get; }

    public ErrorBehavior Behavior { get; }

    [JsonConstructor]
    private Error(IReadOnlyList<ErrorMessage> messages, ErrorType type, ErrorBehavior behavior = ErrorBehavior.Permanent)
    {
        Messages = messages.ToArray();
        Type = type;
        Behavior = behavior;
    }

    private Error(IEnumerable<ErrorMessage> messages, ErrorType type, ErrorBehavior behavior = ErrorBehavior.Permanent)
    {
        Messages = messages.ToArray();
        Type = type;
        Behavior = behavior;
    }

    public string GetMessage() => string.Join(";", Messages.Select(m => m.ToString()));

    public static Error Validation(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.VALIDATION);

    public static Error Validation(IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.VALIDATION);

    public static Error NotFound(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.NOT_FOUND);

    public static Error NotFound(params IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.NOT_FOUND);

    public static Error Failure(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.FAILURE);

    public static Error Failure(IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.FAILURE);

    public static Error Conflict(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.CONFLICT);

    public static Error Conflict(params IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.CONFLICT);

    public static Error Authentication(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.AUTHENTICATION);

    public static Error Authentication(params IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.AUTHENTICATION);

    public static Error Authorization(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.AUTHORIZATION);

    public static Error Authorization(params IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.AUTHORIZATION);

    public static Error RateLimit(string code, string message, string? invalidField = null) =>
        new([new ErrorMessage(code, message, invalidField)], ErrorType.RATE_LIMIT);

    public static Error RateLimit(params IEnumerable<ErrorMessage> messages) =>
        new(messages, ErrorType.RATE_LIMIT);

    public Error AsTransient() => new(Messages, Type, ErrorBehavior.Transient);

    public Error AsPermanent() => new(Messages, Type, ErrorBehavior.Permanent);
    public Exception ToException() => Behavior switch
    {
        ErrorBehavior.Transient => new TransientException(this),
        _ => new PermanentException(this)
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorType
{
    VALIDATION,
    NOT_FOUND,
    FAILURE,
    CONFLICT,
    AUTHENTICATION,
    AUTHORIZATION,
    RATE_LIMIT,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorBehavior
{
    Permanent,
    Transient
}
