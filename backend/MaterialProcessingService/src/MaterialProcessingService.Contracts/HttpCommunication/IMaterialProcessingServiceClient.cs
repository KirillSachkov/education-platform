using CSharpFunctionalExtensions;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;

namespace MaterialProcessingService.Contracts.HttpCommunication;

public interface IMaterialProcessingServiceClient
{
    Task<Result<GetVideoTimecodesResponse, Error>> GetVideoTimecodesAsync(
        Guid videoId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-fetch флагов «есть транскрипт / таймкоды / конспект» для набора видео.
    ///     Использует POST с body, потому что videoIds может быть до сотен на один курс
    ///     и в URL не помещается. Возвращает только статусы — никаких дат/прогресса.
    /// </summary>
    Task<Result<GetVideoArtifactStatusesResponse, Error>> GetArtifactStatusesAsync(
        GetVideoArtifactStatusesRequest request,
        CancellationToken cancellationToken);
}
