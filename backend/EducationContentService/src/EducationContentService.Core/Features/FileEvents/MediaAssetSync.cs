using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Contracts.HttpCommunication;
using EducationContentService.Core.Database;
using Shared.Messaging.IntegrationEvents.Files.Events;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EducationContentService.Core.Features.FileEvents;

/// <summary>
///     Helper для sync-привязки/открепления одиночных media-ассетов
///     (Material/Course/Collection cover/video) в Update-handler'ах.
///     <para>
///     Diff-семантика: <c>previous == requested</c> ⇒ no-op; <c>requested != null</c>
///     ⇒ <see cref="IFileServiceClient.BindAssetAsync"/> + <paramref name="attach"/>.
///     Предыдущий ассет удаляется durable-событием только после успешного save ECS;
///     событие несёт ожидаемую binding revision и безопасно при поздней доставке.
///     <c>requested == null &amp;&amp; previous != null</c> ⇒ <paramref name="detach"/>
///     + durable detach.
///     </para>
///     <para>
///     Caller'ы должны capture <paramref name="previous"/> ДО вызова доменных методов
///     update'а aggregate'а, чтобы diff не зависел от порядка операций внутри Update.
///     </para>
///     Подготовленный bind намеренно не компенсируется синхронным delete при rollback
///     authoritative-транзакции. Пока confirmation не сохранён в outbox, binding неактивен;
///     немедленная компенсация может удалить revision, которую параллельная успешная
///     транзакция уже выбрала, но ещё не успела подтвердить.
/// </summary>
public static class MediaAssetSync
{
    public static async Task<UnitResult<Error>> SyncSingleAssetAsync(
        IFileServiceClient fileServiceClient,
        IOutboxService outbox,
        Guid? previous,
        long previousBindingRevision,
        Guid? requested,
        TargetEntityDto target,
        Guid actorUserId,
        bool actorCanManageAnyAsset,
        Action<Guid, long> attach,
        Action detach,
        CancellationToken cancellationToken)
    {
        if (previous == requested)
            return UnitResult.Success<Error>();

        if (requested is { } newId)
        {
            Guid selectionId = CreateSelectionId(
                target,
                previous,
                previousBindingRevision,
                newId);
            Result<BindAssetResponse, Error> bindResult = await fileServiceClient.BindAssetInternalAsync(
                newId,
                new BindAssetInternalRequest(
                    target,
                    actorUserId,
                    actorCanManageAnyAsset,
                    selectionId),
                cancellationToken);
            if (bindResult.IsFailure)
                return bindResult.Error;

            attach(newId, bindResult.Value.BindingRevision);
            await outbox.PublishAsync(new FileAssetBindingConfirmed(
                newId,
                bindResult.Value.BindingRevision));
            if (previous is { } previousId)
                await outbox.PublishAsync(new FileAssetDetached(previousId, previousBindingRevision));
            return UnitResult.Success<Error>();
        }

        if (previous is { } oldId)
        {
            detach();
            await outbox.PublishAsync(new FileAssetDetached(oldId, previousBindingRevision));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Concurrent writers that observed the same authoritative predecessor are
    ///     retries of one logical selection. They must use one FileService idempotency
    ///     key; otherwise FileService can retain a higher prepared revision after the
    ///     losing ECS transaction is rejected by optimistic concurrency. Including the
    ///     predecessor revision makes a later A→B→A transition a new selection, so an
    ///     old detach cannot delete it.
    /// </summary>
    private static Guid CreateSelectionId(
        TargetEntityDto target,
        Guid? previous,
        long previousBindingRevision,
        Guid requested)
    {
        string fingerprint = string.Create(
            CultureInfo.InvariantCulture,
            $"{target.Type.ToLowerInvariant()}:{target.Id:N}:{previous?.ToString("N") ?? "-"}:{previousBindingRevision}:{requested:N}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return new Guid(hash.AsSpan(0, 16));
    }
}
