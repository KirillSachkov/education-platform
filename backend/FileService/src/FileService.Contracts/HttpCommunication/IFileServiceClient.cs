using FileService.Contracts.Assets;

namespace FileService.Contracts.HttpCommunication;

public interface IFileServiceClient
{
    Task<Result<GetVideoResponse?, Error>> GetVideoAsync(Guid videoId, CancellationToken cancellationToken);

    Task<UnitResult<Error>> UpdateChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Возвращает главы видео из провайдера (Kinescope). Используется backfill-CLI,
    ///     когда нужно денормализовать чапы из Kinescope в ECS <c>chapter_titles</c>.
    /// </summary>
    Task<Result<GetVideoChaptersResponse?, Error>> GetVideoChaptersAsync(
        Guid videoId,
        CancellationToken cancellationToken);

    Task<Result<List<GetPublicVideoResponse>?, Error>> GetVideosBatchAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken);

    Task<Result<GetActiveAssetSlotResponse?, Error>> GetActiveAssetSlotAsync(
        string entityType,
        Guid entityId,
        string usageType,
        CancellationToken cancellationToken);

    Task<Result<GetFileResponse?, Error>> GetFileAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Result<List<GetFileResponse>?, Error>> GetFilesByEntityAsync(
        Guid entityId, string entityType, CancellationToken cancellationToken);

    Task<Result<List<GetFileResponse>?, Error>> GetFilesBatchAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken);

    Task<UnitResult<Error>> BindDraftAssetsAsync(
        BindDraftAssetsRequest request, CancellationToken cancellationToken);

    Task<UnitResult<Error>> SyncEntityAssetsAsync(
        SyncEntityAssetsRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Привязывает один ассет к целевой сущности синхронно. Идемпотентен.
    ///     Используется ECS при создании/обновлении Material/Course/Collection,
    ///     Возвращает монотонную binding revision, которую ECS сохраняет рядом
    ///     с media-id для optimistic concurrency и безопасного delayed detach.
    /// </summary>
    Task<Result<BindAssetResponse, Error>> BindAssetAsync(
        Guid assetId, BindAssetRequest request, CancellationToken cancellationToken);

    Task<Result<BindAssetResponse, Error>> BindAssetInternalAsync(
        Guid assetId,
        BindAssetInternalRequest request,
        CancellationToken cancellationToken);

    /// <summary>Помечает ассет на удаление синхронно. Idempotent.</summary>
    Task<UnitResult<Error>> DetachAssetAsync(Guid assetId, CancellationToken cancellationToken);

    Task<UnitResult<Error>> ReassignAssetsOwnerAsync(
        ReassignAssetOwnerRequest request,
        CancellationToken cancellationToken);
}