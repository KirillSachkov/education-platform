using Core.Database;
using FileService.Core.FilesStorage;
using FileService.Core.Imaging;
using FileService.Core.Repositories;
using FileService.Domain;

namespace FileService.Core.Services.Files;

/// <summary>
///     Generates responsive WebP variants for a Ready image asset (issue #646):
///     download original from S3 → resize to each in-scope width → upload variant
///     objects → record the variant set on the asset → save. Only-downscale (widths
///     ≥ the original are skipped). Idempotent: re-running overwrites the prior set
///     and re-uploads the same deterministic keys, so a retry is safe.
/// </summary>
public sealed class ImageVariantGenerationService
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IFileStorageRefRepository _fileStorageRefRepository;
    private readonly IObjectStorageProvider _objectStorageProvider;
    private readonly IImageVariantRenderer _renderer;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<ImageVariantGenerationService> _logger;

    public ImageVariantGenerationService(
        IMediaAssetRepository assetRepository,
        IFileStorageRefRepository fileStorageRefRepository,
        IObjectStorageProvider objectStorageProvider,
        IImageVariantRenderer renderer,
        ITransactionManager transactionManager,
        ILogger<ImageVariantGenerationService> logger)
    {
        _assetRepository = assetRepository;
        _fileStorageRefRepository = fileStorageRefRepository;
        _objectStorageProvider = objectStorageProvider;
        _renderer = renderer;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    /// <summary>
    ///     Generates (or regenerates) variants for the asset. No-op (success) when the
    ///     asset is gone, not an eligible image, or the original is smaller than the
    ///     smallest bucket. Returns the number of variants recorded.
    /// </summary>
    public async Task<Result<int, Error>> GenerateAsync(Guid assetId, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(a => a.Id == assetId, cancellationToken);
        if (assetResult.IsFailure)
        {
            // Asset deleted between enqueue and handle — nothing to do.
            _logger.LogDebug("GenerateImageVariants: asset {AssetId} not found, skipping", assetId);
            return 0;
        }

        MediaAsset asset = assetResult.Value;
        if (!asset.IsImageVariantEligible())
        {
            _logger.LogDebug(
                "GenerateImageVariants: asset {AssetId} not an eligible image (kind={Kind}, status={Status}, contentType={ContentType}), skipping",
                assetId, asset.Kind, asset.Status, asset.ContentType.Value);
            return 0;
        }

        Result<FileStorageRef, Error> storageRefResult =
            await _fileStorageRefRepository.GetByAsync(r => r.AssetId == asset.Id, cancellationToken);
        if (storageRefResult.IsFailure)
        {
            _logger.LogWarning("GenerateImageVariants: storage ref missing for asset {AssetId}", assetId);
            return storageRefResult.Error;
        }

        string originalKey = storageRefResult.Value.StorageKey.Value;

        Result<byte[], Error> downloadResult =
            await _objectStorageProvider.DownloadObjectAsync(originalKey, cancellationToken);
        if (downloadResult.IsFailure)
        {
            _logger.LogWarning(
                "GenerateImageVariants: failed to download original for asset {AssetId} ({Key}): {ErrorType}",
                assetId, originalKey, downloadResult.Error.Type);
            return downloadResult.Error;
        }

        byte[] originalBytes = downloadResult.Value;

        cancellationToken.ThrowIfCancellationRequested();
        Result<int, Error> widthResult = _renderer.ReadWidth(originalBytes);
        if (widthResult.IsFailure)
        {
            _logger.LogWarning(
                "GenerateImageVariants: failed to read width for asset {AssetId}: {ErrorType}",
                assetId, widthResult.Error.Type);
            return widthResult.Error;
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> widthsToGenerate = ImageVariantPolicy.WidthsToGenerate(widthResult.Value);
        if (widthsToGenerate.Count == 0)
        {
            _logger.LogInformation(
                "GenerateImageVariants: original {AssetId} width {Width}px is below the smallest bucket — no variants generated",
                assetId, widthResult.Value);
            // Persist an empty set so the serve path / backfill treats it as processed.
            asset.SetImageVariants([]);
            UnitResult<Error> emptySave = await _transactionManager.SaveChangesAsync(cancellationToken);
            return emptySave.IsFailure ? emptySave.Error : 0;
        }

        var variants = new List<ImageVariant>(widthsToGenerate.Count);
        foreach (int width in widthsToGenerate)
        {
            Result<StorageKey, Error> variantKeyResult = ImageVariantPolicy.VariantStorageKey(asset.Id, width);
            if (variantKeyResult.IsFailure)
            {
                return variantKeyResult.Error;
            }

            Result<RenderedImage, Error> renderResult =
                await _renderer.RenderWebpAsync(originalBytes, width, cancellationToken);
            if (renderResult.IsFailure)
            {
                _logger.LogWarning(
                    "GenerateImageVariants: failed to render {Width}px variant for asset {AssetId}: {ErrorType}",
                    width, assetId, renderResult.Error.Type);
                return renderResult.Error;
            }

            RenderedImage rendered = renderResult.Value;
            string variantKey = variantKeyResult.Value.Value;

            cancellationToken.ThrowIfCancellationRequested();
            UnitResult<Error> uploadResult = await _objectStorageProvider.UploadObjectAsync(
                variantKey,
                rendered.Content,
                rendered.ContentType,
                cancellationToken);
            if (uploadResult.IsFailure)
            {
                _logger.LogWarning(
                    "GenerateImageVariants: failed to upload {Width}px variant for asset {AssetId}: {ErrorType}",
                    width, assetId, uploadResult.Error.Type);
                return uploadResult.Error;
            }

            variants.Add(new ImageVariant(width, variantKey, rendered.ContentType, rendered.Content.LongLength));
        }

        cancellationToken.ThrowIfCancellationRequested();
        asset.SetImageVariants(variants);
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "GenerateImageVariants: failed to persist variants for asset {AssetId}: {ErrorType}",
                assetId, saveResult.Error.Type);
            return saveResult.Error;
        }

        _logger.LogInformation(
            "GenerateImageVariants: recorded {Count} variant(s) for asset {AssetId}",
            variants.Count, assetId);

        return variants.Count;
    }
}
