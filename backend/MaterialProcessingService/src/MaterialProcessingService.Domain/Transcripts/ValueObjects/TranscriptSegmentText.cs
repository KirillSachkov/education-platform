using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.Transcripts.ValueObjects;

public sealed record TranscriptSegmentText
{
    public const int MAX_LENGTH = 10_000;

    private TranscriptSegmentText(string value) => Value = value;

    public string Value { get; }

    public static Result<TranscriptSegmentText, Error> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GeneralErrors.ValueIsInvalid(nameof(TranscriptSegmentText));

        string trimmed = value.Trim();
        if (trimmed.Length > MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid(nameof(TranscriptSegmentText));

        return new TranscriptSegmentText(trimmed);
    }
}
