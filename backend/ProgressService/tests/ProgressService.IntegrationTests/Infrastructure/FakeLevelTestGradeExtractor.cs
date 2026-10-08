using System.Collections.ObjectModel;
using CSharpFunctionalExtensions;
using ProgressService.Core.Features.LevelTests.AiGrading;
using Shared.AI;
using Shared.AI.Skills;
using SharedKernel;

namespace ProgressService.IntegrationTests.Infrastructure;

/// <summary>
///     Скриптуемый фейк AI seam'а грейдинга level-test'а (ST-5, #480) —
///     <see cref="IStructuredExtractor{TPayload}"/> для <see cref="LevelTestAiGradesResponse"/>:
///     самый тонкий интерфейс, который потребляет handler (JSON-schema/сериализация
///     провайдера не задействуются). Per-test сценарии: успешный payload / failure /
///     throw (sticky — на все вызовы подряд, для тестов ретраев). Считает вызовы.
/// </summary>
public sealed class FakeLevelTestGradeExtractor : IStructuredExtractor<LevelTestAiGradesResponse>
{
    private readonly Queue<Func<Result<AiStructured<LevelTestAiGradesResponse>, Error>>> _responses = new();
    private readonly Lock _lock = new();

    private Func<Result<AiStructured<LevelTestAiGradesResponse>, Error>>? _sticky;
    private int _invocations;

    public int Invocations => _invocations;

    public Collection<StructuredExtractRequest> ReceivedRequests { get; } = [];

    public void Reset()
    {
        lock (_lock)
        {
            _responses.Clear();
            ReceivedRequests.Clear();
            _sticky = null;
            _invocations = 0;
        }
    }

    public void QueueGrades(params LevelTestAiGradeItem[] grades)
    {
        var response = new LevelTestAiGradesResponse(grades.ToList());
        lock (_lock)
        {
            _responses.Enqueue(() => Result.Success<AiStructured<LevelTestAiGradesResponse>, Error>(
                new AiStructured<LevelTestAiGradesResponse>(
                    response,
                    new AiUsage(InputTokens: 1000, OutputTokens: 100, TotalTokens: 1100),
                    AiFinishReason.Stop)));
        }
    }

    /// <summary>Один и тот же failure на ВСЕ вызовы — для тестов ретраев.</summary>
    public void QueueFailureSticky(Error error)
    {
        lock (_lock)
        {
            _sticky = () => error;
        }
    }

    /// <summary>Исключение на ВСЕ вызовы — для тестов ретраев (handler ловит, не падает).</summary>
    public void QueueThrowSticky(Exception exception)
    {
        lock (_lock)
        {
            _sticky = () => throw exception;
        }
    }

    public Task<Result<AiStructured<LevelTestAiGradesResponse>, Error>> ExtractAsync(
        StructuredExtractRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _invocations++;
            ReceivedRequests.Add(request);

            if (_sticky is not null)
            {
                return Task.FromResult(_sticky());
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "FakeLevelTestGradeExtractor: ответ не заскриптован — вызови QueueGrades/QueueFailureSticky/QueueThrowSticky в тесте.");
            }

            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
