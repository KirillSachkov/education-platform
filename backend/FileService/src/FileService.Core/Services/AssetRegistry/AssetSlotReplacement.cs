using FileService.Domain;
using FileService.Core.Database;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Services.AssetRegistry;

/// <summary>
///     Завершает replacement только после durable подтверждения ECS, что ссылка
///     на predecessor больше не является активной.
/// </summary>
public interface IAssetSlotReplacement
{
    Task<bool> RequestDeleteAsync(MediaAsset asset);
}

public sealed class AssetSlotReplacement : IAssetSlotReplacement
{
    private readonly IOutboxService _outbox;
    private readonly ILogger<AssetSlotReplacement> _logger;

    public AssetSlotReplacement(
        IOutboxService outbox,
        ILogger<AssetSlotReplacement> logger)
    {
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<bool> RequestDeleteAsync(MediaAsset asset)
    {
        UnitResult<Error> deleteResult = asset.RequestDelete();
        if (deleteResult.IsFailure)
        {
            _logger.LogWarning(
                "Asset deletion skipped for {AssetId}: {ErrorType}",
                asset.Id,
                deleteResult.Error.Type);
            return false;
        }

        await _outbox.PublishAsync(new FileAssetDeleted(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            asset.TargetEntity?.Id,
            asset.TargetEntity?.Type,
            asset.GetDeletionBindingRevision()));
        return true;
    }
}
