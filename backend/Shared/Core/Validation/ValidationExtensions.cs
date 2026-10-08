using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FluentValidation.Results;
using SharedKernel;

namespace Core.Validation;

public static class ValidationExtensions
{
    public static Error ToError(this ValidationResult validationResult)
    {
        IEnumerable<ErrorMessage> messages = validationResult.Errors.SelectMany(ToErrorMessages);

        return Error.Validation(messages);
    }

    // Правила через .WithError(...) кладут в ErrorMessage JSON-сериализованный Error, а правила
    // с дефолтными сообщениями FluentValidation — обычный текст. Раньше ToError десериализовал
    // ЛЮБОЕ сообщение как Error и падал JsonException'ом на дефолтном тексте: 400 превращался в
    // 500. Теперь нераспарсенное сообщение заворачивается в обычное validation-сообщение.
    private static IReadOnlyList<ErrorMessage> ToErrorMessages(ValidationFailure failure)
    {
        if (TryParseError(failure.ErrorMessage, out Error? error))
        {
            return error.Messages;
        }

        return [new ErrorMessage("validation.failure", failure.ErrorMessage, failure.PropertyName)];
    }

    private static bool TryParseError(string message, [NotNullWhen(true)] out Error? error)
    {
        try
        {
            error = JsonSerializer.Deserialize<Error>(message);
            return error is not null;
        }
        catch (JsonException)
        {
            error = null;
            return false;
        }
    }
}
