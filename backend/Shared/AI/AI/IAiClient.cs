using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI;

public interface IAiClient
{
    Task<Result<AiBudgetAnalysis, Error>> AnalyzeAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken);

    Task<Result<AiGenerationResult<TResponse>, Error>> GenerateAsync<TResponse>(
        AiGenerationRequest request,
        CancellationToken cancellationToken);
}
