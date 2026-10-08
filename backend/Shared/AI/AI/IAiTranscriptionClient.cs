using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI;

/// <summary>
///     Провайдер-агностик контракт для STT через dedicated transcription API
///     (OpenAI Whisper-style <c>/v1/audio/transcriptions</c>). Отделён от
///     <see cref="IAiClient"/> намеренно: chat-API (с audio input) и transcription API
///     это разные endpoint'ы с разными формами запроса/ответа. Provider адаптеры
///     (RouterAI/Polza) реализуют через OpenAI SDK либо прямой multipart/form-data POST.
/// </summary>
public interface IAiTranscriptionClient
{
    /// <summary>
    ///     Транскрибирует один аудио-файл (≤25 МБ per OpenAI limit) и возвращает
    ///     список сегментов с timestamps. Сегментация и язык определяются провайдером;
    ///     <paramref name="languageHint"/> опциональный (например "ru") — ускоряет
    ///     распознавание и снижает hallucination на технических терминах.
    /// </summary>
    Task<Result<AiTranscriptionResult, Error>> TranscribeAsync(
        AiTranscriptionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AiTranscriptionRequest(
    string Model,
    string FileName,
    string ContentType,
    IReadOnlyList<byte> AudioBytes,
    string? LanguageHint = null,
    string? Prompt = null,
    int? TimeoutSeconds = null);

public sealed record AiTranscriptionResult(
    string Provider,
    string Model,
    string Language,
    string FullText,
    IReadOnlyList<AiTranscriptionSegment> Segments,
    double DurationSeconds);

public sealed record AiTranscriptionSegment(
    double StartSeconds,
    double EndSeconds,
    string Text);
