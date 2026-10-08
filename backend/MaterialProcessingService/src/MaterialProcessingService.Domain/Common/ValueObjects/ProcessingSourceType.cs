using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Common.ValueObjects;

public sealed record ProcessingSourceType
{
    public const int MAX_LENGTH = 64;

    private ProcessingSourceType(string value) => Value = value;

    public string Value { get; }

    public static Result<ProcessingSourceType, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GeneralErrors.ValueIsRequired(nameof(ProcessingSourceType));

        if (value.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid(nameof(ProcessingSourceType));

        return new ProcessingSourceType(value);
    }
}
