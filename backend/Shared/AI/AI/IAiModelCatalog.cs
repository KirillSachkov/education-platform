using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI;

public interface IAiModelCatalog
{
    Task<Result<AiModelInfo, Error>> GetModelAsync(
        string model,
        CancellationToken cancellationToken);
}
