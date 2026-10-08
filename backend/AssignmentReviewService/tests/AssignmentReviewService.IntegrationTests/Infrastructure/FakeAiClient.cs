using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using CSharpFunctionalExtensions;
using Shared.AI;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

/// <summary>
///     Фейковый IAiClient для интеграционных тестов Phase 7. По-умолчанию возвращает
///     valid AI review JSON; per-test можно подменить ответ через
///     <see cref="QueueResponse"/> или <see cref="QueueFailure"/>.
/// </summary>
public sealed class FakeAiClient : IAiClient
{
    private readonly Queue<Func<Result<AiGenerationResult<JsonElement>, Error>>> _responses = new();
    private readonly Lock _lock = new();

    // Sticky-ответ (#405): если задан, возвращается на КАЖДЫЙ вызов (queue игнорируется).
    // Нужен для тестов ретраев — failure/throw должны повторяться на всех попытках, а не
    // сменяться default-успехом из пустой очереди на 2-й попытке.
    private Func<Result<AiGenerationResult<JsonElement>, Error>>? _sticky;

    public Collection<AiGenerationRequest> ReceivedRequests { get; } = [];

    public void Reset()
    {
        lock (_lock)
        {
            _responses.Clear();
            ReceivedRequests.Clear();
            _sticky = null;
        }
    }

    public void QueueResponse(string verdict, string summary, params (string Path, int Line, string Body)[] inlineComments)
    {
        var dto = new
        {
            verdict,
            summary,
            inline_comments = inlineComments.Select(c => new
            {
                path = c.Path,
                line = c.Line,
                body = c.Body,
                suggestion = (string?)null,
            }).ToArray(),
        };

        string json = JsonSerializer.Serialize(dto);
        JsonElement element = JsonDocument.Parse(json).RootElement.Clone();

        lock (_lock)
        {
            _responses.Enqueue(() => Result.Success<AiGenerationResult<JsonElement>, Error>(
                new AiGenerationResult<JsonElement>(
                    element,
                    "openai-compat",
                    "openai/gpt-4.1-mini",
                    new AiUsage(InputTokens: 1500, OutputTokens: 200, TotalTokens: 1700),
                    AiFinishReason.Stop)));
        }
    }

    /// <summary>
    ///     #798 — ответ с need_files: модель просит файлы репозитория вместо вердикта
    ///     (verdict/summary при непустом need_files цикл игнорирует, но схема требует их).
    /// </summary>
    public void QueueResponseWithNeedFiles(
        string verdict, string summary, string[] needFiles,
        params (string Path, int Line, string Body)[] inlineComments)
    {
        var dto = new
        {
            verdict,
            summary,
            inline_comments = inlineComments.Select(c => new
            {
                path = c.Path,
                line = c.Line,
                body = c.Body,
                suggestion = (string?)null,
            }).ToArray(),
            need_files = needFiles,
        };

        string json = JsonSerializer.Serialize(dto);
        JsonElement element = JsonDocument.Parse(json).RootElement.Clone();

        lock (_lock)
        {
            _responses.Enqueue(() => Result.Success<AiGenerationResult<JsonElement>, Error>(
                new AiGenerationResult<JsonElement>(
                    element,
                    "openai-compat",
                    "openai/gpt-4.1-mini",
                    new AiUsage(InputTokens: 1500, OutputTokens: 200, TotalTokens: 1700),
                    AiFinishReason.Stop)));
        }
    }

    public void QueueRawJson(string rawJson)
    {
        JsonElement element = JsonDocument.Parse(rawJson).RootElement.Clone();
        lock (_lock)
        {
            _responses.Enqueue(() => Result.Success<AiGenerationResult<JsonElement>, Error>(
                new AiGenerationResult<JsonElement>(
                    element,
                    "openai-compat",
                    "openai/gpt-4.1-mini",
                    new AiUsage(0, 0, 0),
                    AiFinishReason.Stop)));
        }
    }

    public void QueueFailure(Error error)
    {
        lock (_lock)
        {
            _responses.Enqueue(() => error);
        }
    }

    /// <summary>Sticky-фейл (#405): один и тот же error на ВСЕ вызовы — для тестов ретраев.</summary>
    public void QueueFailureSticky(Error error)
    {
        lock (_lock)
        {
            _sticky = () => error;
        }
    }

    /// <summary>Sticky-throw (#405): исключение на ВСЕ вызовы — для тестов ретраев.</summary>
    public void QueueThrowSticky(Exception ex)
    {
        lock (_lock)
        {
            _sticky = () => throw ex;
        }
    }

    /// <summary>
    ///     Simulate the AI client THROWING (vs returning a failure Result) — e.g. an
    ///     <see cref="OperationCanceledException"/> on request-token cancellation/timeout.
    /// </summary>
    public void QueueThrow(Exception ex)
    {
        lock (_lock)
        {
            _responses.Enqueue(() => throw ex);
        }
    }

    public Task<Result<AiBudgetAnalysis, Error>> AnalyzeAsync(
        AiGenerationRequest request, CancellationToken cancellationToken)
    {
        // Phase 7 не вызывает Analyze; даём заглушку чтобы не падать если позовут.
        return Task.FromResult(Result.Success<AiBudgetAnalysis, Error>(
            new AiBudgetAnalysis(
                Provider: "openai-compat",
                Model: request.Model,
                ContextWindowTokens: 128_000,
                EstimatedInputTokens: 1000,
                ReservedOutputTokens: request.MaxOutputTokens ?? 4000,
                Fits: true,
                EstimateConfidence: AiTokenEstimateConfidence.High)));
    }

    public Task<Result<AiGenerationResult<TResponse>, Error>> GenerateAsync<TResponse>(
        AiGenerationRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            ReceivedRequests.Add(request);

            if (typeof(TResponse) != typeof(JsonElement))
            {
                throw new InvalidOperationException(
                    $"FakeAiClient supports only JsonElement responses (Phase 7 contract). Got {typeof(TResponse).Name}.");
            }

            if (_sticky is null && _responses.Count == 0)
            {
                // Default — valid LOOKS_GOOD response.
                QueueResponse("LOOKS_GOOD", "Fake default verdict.");
            }

            Func<Result<AiGenerationResult<JsonElement>, Error>> next = _sticky ?? _responses.Dequeue();
            Result<AiGenerationResult<JsonElement>, Error> result = next();
            // Cast safe — мы только что проверили typeof(TResponse) == JsonElement.
            return Task.FromResult((Result<AiGenerationResult<TResponse>, Error>)(object)result);
        }
    }
}
