using Core.Database;
using FileService.Core.Repositories;
using FileService.Domain;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.EventHandlers;

public static class CourseAssetOwnershipChangedHandler
{
    public static async Task HandleAsync(
        CourseAssetOwnershipChanged message,
        IAssetOwnershipCheckpointRepository checkpointRepository,
        IMediaAssetRepository assetRepository,
        ITransactionManager transactionManager,
        IOptions<TargetEntityOptions> targetOptions,
        ILogger logger,
        CancellationToken ct)
    {
        if (message.CourseId == Guid.Empty || message.NewOwnerId == Guid.Empty || message.OwnershipRevision <= 0)
        {
            throw new InvalidOperationException("Invalid course asset ownership event.");
        }

        UnitResult<Error> begin = await transactionManager.BeginTransactionAsync(ct);
        if (begin.IsFailure)
        {
            throw begin.Error.AsTransient().ToException();
        }

        await checkpointRepository.AcquireCourseLockAsync(message.CourseId, ct);

        var targets = new List<TargetEntity>(message.Targets.Count);
        foreach (AssetOwnershipTarget item in message.Targets.Distinct())
        {
            Result<TargetEntity, Error> target = TargetEntity.Of(item.Type, item.Id);
            if (target.IsFailure || !targetOptions.Value.AllowedTargetEntityTypes.Contains(
                    item.Type,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid target in course asset ownership event.");
            }

            targets.Add(target.Value);
        }

        await checkpointRepository.AcquireTargetLocksAsync(targets, ct);

        IReadOnlyList<AssetOwnershipCheckpoint> checkpoints =
            await checkpointRepository.GetManyAsync(targets, ct);
        Dictionary<(string Type, Guid Id), AssetOwnershipCheckpoint> latestCheckpointByTarget = checkpoints
            .GroupBy(checkpoint => (checkpoint.TargetType, checkpoint.TargetId))
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(checkpoint => checkpoint.LastAppliedRevision)!);
        Dictionary<(string Type, Guid Id), AssetOwnershipCheckpoint> courseCheckpointsByTarget = checkpoints
            .Where(checkpoint => checkpoint.CourseId == message.CourseId)
            .ToDictionary(checkpoint => (checkpoint.TargetType, checkpoint.TargetId));
        TargetEntity[] applicableTargets = targets
            .Where(target => !latestCheckpointByTarget.TryGetValue(
                                 (target.Type, target.Id),
                                 out AssetOwnershipCheckpoint? checkpoint)
                             || checkpoint.LastAppliedRevision < message.OwnershipRevision)
            .ToArray();

        if (applicableTargets.Length == 0)
        {
            UnitResult<Error> noOpCommit = await transactionManager.CommitTransactionAsync(ct);
            if (noOpCommit.IsFailure)
                throw noOpCommit.Error.AsTransient().ToException();

            return;
        }

        List<MediaAsset> assets = await assetRepository.GetByTargetEntitiesAsync(applicableTargets, ct);
        foreach (MediaAsset asset in assets)
        {
            UnitResult<Error> reassign = asset.ReassignOwner(message.NewOwnerId);
            if (reassign.IsFailure)
            {
                throw reassign.Error.ToException();
            }
        }

        foreach (TargetEntity target in applicableTargets)
        {
            if (courseCheckpointsByTarget.TryGetValue(
                    (target.Type, target.Id),
                    out AssetOwnershipCheckpoint? checkpoint))
            {
                checkpoint.AdvanceTo(message.OwnershipRevision, message.NewOwnerId);
                continue;
            }

            await checkpointRepository.AddAsync(new AssetOwnershipCheckpoint(
                message.CourseId,
                target.Type,
                target.Id,
                message.NewOwnerId,
                message.OwnershipRevision), ct);
        }

        UnitResult<Error> commit = await transactionManager.CommitTransactionAsync(ct);
        if (commit.IsFailure)
        {
            throw commit.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Applied course asset ownership revision {Revision} for course {CourseId}; owner {OwnerId}, targets {TargetCount}, assets {AssetCount}",
            message.OwnershipRevision,
            message.CourseId,
            message.NewOwnerId,
            applicableTargets.Length,
            assets.Count);
    }
}
