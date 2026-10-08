using System.Data.Common;
using Core.Database;
using Dapper;
using EducationContentService.Core.Database;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Wolverine;

namespace EducationContentService.Core.Features.FileEvents;

/// <summary>
///     Delayed authoritative reconciliation for FileService binding attempts.
///     The delay lets the HTTP request that prepared the binding commit or roll back;
///     the check then reads the committed aggregate and either confirms its exact
///     revision or detaches the abandoned candidate.
/// </summary>
public sealed record VerifyFileAssetBinding(
    Guid AssetId,
    string UsageType,
    Guid TargetEntityId,
    string TargetEntityType,
    long CandidateRevision);

public static class BindingVerificationPolicy
{
    public static readonly TimeSpan SynchronousBindDelay = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan UploadCandidateDelay = TimeSpan.FromHours(24);

    public static TimeSpan? GetDelay(FileAssetBound message)
    {
        if (!Supports(message.TargetEntityType, message.UsageType))
            return null;

        return message.RequiresAuthoritativeConfirmation
            ? SynchronousBindDelay
            : UploadCandidateDelay;
    }

    public static bool Supports(string targetEntityType, string usageType) =>
        (targetEntityType, usageType) is
            ("material", "material_video") or
            ("material", "material_preview") or
            ("course", "course_video") or
            ("course", "course_preview") or
            ("collection", "collection_cover");
}

public sealed class VideoUploadInitiatedBindingVerificationScheduler(IMessageBus messageBus)
{
    public async Task Handle(VideoUploadInitiated message)
    {
        if (!BindingVerificationPolicy.Supports(message.TargetEntityType, message.UsageType))
            return;

        await messageBus.ScheduleAsync(
            new VerifyFileAssetBinding(
                message.AssetId,
                message.UsageType,
                message.TargetEntityId,
                message.TargetEntityType,
                CandidateRevision: 0),
            BindingVerificationPolicy.UploadCandidateDelay);
    }
}

public sealed class VerifyFileAssetBindingHandler
{
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;

    public VerifyFileAssetBindingHandler(
        ITransactionManager transactionManager,
        IOutboxService outbox)
    {
        _transactionManager = transactionManager;
        _outbox = outbox;
    }

    public async Task Handle(VerifyFileAssetBinding message, CancellationToken cancellationToken)
    {
        string? sql = (message.TargetEntityType, message.UsageType) switch
        {
            ("material", "material_video") =>
                "SELECT video_id AS AssetId, video_binding_revision AS BindingRevision FROM materials WHERE id = @TargetEntityId",
            ("material", "material_preview") =>
                "SELECT image_id AS AssetId, image_binding_revision AS BindingRevision FROM materials WHERE id = @TargetEntityId",
            ("course", "course_video") =>
                "SELECT video_id AS AssetId, video_binding_revision AS BindingRevision FROM courses WHERE id = @TargetEntityId",
            ("course", "course_preview") =>
                "SELECT image_id AS AssetId, image_binding_revision AS BindingRevision FROM courses WHERE id = @TargetEntityId",
            ("collection", "collection_cover") =>
                "SELECT cover_image_id AS AssetId, cover_binding_revision AS BindingRevision FROM collections WHERE id = @TargetEntityId",
            _ => null,
        };

        if (sql is null)
            return;

        DbConnection connection = _transactionManager.GetDbConnection();
        BindingRow? authoritative = await connection.QuerySingleOrDefaultAsync<BindingRow>(
            new CommandDefinition(
                sql,
                new { message.TargetEntityId },
                cancellationToken: cancellationToken));

        if (authoritative?.AssetId == message.AssetId)
        {
            await _outbox.PublishAsync(new FileAssetBindingConfirmed(
                message.AssetId,
                authoritative.BindingRevision));
        }
        else
        {
            await _outbox.PublishAsync(new FileAssetDetached(
                message.AssetId,
                message.CandidateRevision));
        }

        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();
    }

    private sealed record BindingRow(Guid? AssetId, long BindingRevision);
}
