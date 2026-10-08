using CSharpFunctionalExtensions;
using FileService.Core.Repositories;
using FileService.Core.Services.Files;
using FileService.Domain;
using SharedKernel;

namespace FileService.Web.Configuration;

/// <summary>
///     Backfill CLI (#646): generates responsive image variants for existing READY image
///     assets that don't have them yet. Idempotent — skips assets already carrying variants,
///     and re-running is safe (generation overwrites deterministically).
///
///     Usage: dotnet FileService.Web.dll generate-image-variants
///
///     Runs generation directly (scope-per-asset) rather than via the outbox so the operator
///     gets a synchronous progress count. Existing covers/avatars/markdown images benefit.
/// </summary>
public static class GenerateImageVariantsCli
{
    public const string CommandName = "generate-image-variants";

    private const int BATCH_SIZE = 100;

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        DateTime cursor = DateTime.MinValue.ToUniversalTime();
        int processed = 0;
        int generated = 0;
        int skipped = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            List<MediaAsset> batch;
            await using (AsyncServiceScope readScope = services.CreateAsyncScope())
            {
                IMediaAssetRepository repository =
                    readScope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();
                batch = await repository.GetReadyFileAssetsBatchAsync(BATCH_SIZE, cursor, cancellationToken);
            }

            if (batch.Count == 0)
            {
                break;
            }

            // Advance the cursor by the true last row of the SQL page (before image filtering)
            // so a page of non-image files can't stall the loop.
            cursor = batch[^1].CreatedAt;

            foreach (MediaAsset asset in batch)
            {
                if (!asset.IsImageVariantEligible())
                {
                    continue;
                }

                processed++;

                if (asset.HasImageVariants())
                {
                    skipped++;
                    continue;
                }

                // Fresh scope per asset — keeps the DbContext / transaction small and
                // isolates a single failing asset from the rest of the run.
                await using AsyncServiceScope genScope = services.CreateAsyncScope();
                ImageVariantGenerationService generation =
                    genScope.ServiceProvider.GetRequiredService<ImageVariantGenerationService>();

                Result<int, Error> result = await generation.GenerateAsync(asset.Id, cancellationToken);
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "generate-image-variants: failed for asset {AssetId}: {ErrorType} {ErrorMessage}",
                        asset.Id, result.Error.Type, result.Error.GetMessage());
                    continue;
                }

                generated++;
            }

            // Per-batch heartbeat so an operator can watch a long one-time backfill make
            // progress instead of staring at a silent console until the final summary.
            logger.LogInformation(
                "generate-image-variants: batch done (cursor={Cursor:o}). running totals — processed={Processed}, generated={Generated}, skipped(existing)={Skipped}",
                cursor, processed, generated, skipped);
        }

        logger.LogInformation(
            "generate-image-variants: done. processed={Processed}, generated={Generated}, skipped(existing)={Skipped}",
            processed, generated, skipped);
    }
}
