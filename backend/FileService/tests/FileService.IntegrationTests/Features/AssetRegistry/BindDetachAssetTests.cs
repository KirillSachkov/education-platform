using System.Net;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Repositories;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BindDetachAssetTests : FileServiceTestsBase
{
    public BindDetachAssetTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task BindAsset_DraftPreview_BindsToMaterialAndClearsDraft()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == preview.AssetId);
            Assert.False(asset.IsTemporary);
            Assert.Null(asset.DraftId);
            Assert.NotNull(asset.TargetEntity);
            Assert.Equal("material", asset.TargetEntity!.Type);
            Assert.Equal(materialId, asset.TargetEntity.Id);
        });
    }

    [Fact]
    public async Task BindAsset_ReadyMaterialVideo_PublishesReadyAfterAggregateConfirmation()
    {
        Guid materialId = Guid.NewGuid();
        InitiateVideoUploadResponse video =
            await InitiateDraftVideoAsync(Guid.NewGuid(), "ready-bind.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == video.AssetId);
            asset.MarkProcessing();
            asset.MarkReady();
            await db.SaveChangesAsync();
        });
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{video.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        FileAssetBound prepared = Assert.Single(OutboxCollector.OfType<FileAssetBound>());
        Assert.Equal(video.AssetId, prepared.AssetId);
        Assert.True(prepared.RequiresAuthoritativeConfirmation);
        Assert.Empty(OutboxCollector.OfType<VideoReadyForProcessing>());
        long revision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == video.AssetId)).BindingRevision);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(video.AssetId, revision));
        Assert.Equal(
            video.AssetId,
            Assert.Single(OutboxCollector.OfType<VideoReadyForProcessing>()).AssetId);
    }

    [Fact]
    public async Task BindAsset_AlreadyBoundToSameTarget_ReturnsSuccessWithNewRevision()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        BindAssetRequest payload = new(new TargetEntityDto("material", materialId));

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind", payload);
        long firstRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind", payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        long secondRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        Assert.True(secondRevision > firstRevision);
    }

    [Fact]
    public async Task BindAsset_SameSelectionId_ReturnsOriginalRevisionWithoutRepublishing()
    {
        Guid materialId = Guid.NewGuid();
        Guid selectionId = Guid.CreateVersion7();
        InitiateFileUploadResponse preview =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "idempotent-bind.png");
        BindAssetRequest request = new(
            new TargetEntityDto("material", materialId),
            selectionId);

        (await AppHttpClient.PostAsJsonAsync($"/files/{preview.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long firstRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        OutboxCollector.Clear();

        (await AppHttpClient.PostAsJsonAsync($"/files/{preview.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();

        long secondRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        Assert.Equal(firstRevision, secondRevision);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
    }

    [Fact]
    public async Task BindAsset_DirectAuthorCannotAdvanceSameTargetRevision()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");
        InitiateFileUploadResponse preview =
            await InitiateAndCompleteDraftPreviewAsync(Guid.CreateVersion7(), "direct-retry.png");

        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId), Guid.CreateVersion7())))
            .EnsureSuccessStatusCode();
        long originalRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        OutboxCollector.Clear();

        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId), Guid.CreateVersion7())))
            .EnsureSuccessStatusCode();

        long retryRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        Assert.Equal(originalRevision, retryRevision);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
    }

    [Fact]
    public async Task BindAsset_AlreadyBoundToDifferentTarget_ReturnsConflict()
    {
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        string body = await second.Content.ReadAsStringAsync();
        Assert.Contains("asset.already.bound", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindAsset_AsNonOwner_Returns403()
    {
        Guid userA = Guid.NewGuid();
        AuthenticateAs(userA, "platform-author");

        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        Guid userB = Guid.NewGuid();
        AuthenticateAs(userB, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("asset.bind.not.owner", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindAsset_SingleAssetReplacement_DeletesPreviousOnlyAfterRevisionMatchedConfirmation()
    {
        Guid materialId = Guid.NewGuid();

        InitiateFileUploadResponse first = await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "old.png");
        HttpResponseMessage firstBind = await AppHttpClient.PostAsJsonAsync(
            $"/files/{first.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));
        Assert.Equal(HttpStatusCode.OK, firstBind.StatusCode);

        InitiateFileUploadResponse second = await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "new.png");
        HttpResponseMessage secondBind = await AppHttpClient.PostAsJsonAsync(
            $"/files/{second.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));
        Assert.Equal(HttpStatusCode.OK, secondBind.StatusCode);

        long previousRevision = await ExecuteInDb(async db =>
        {
            MediaAsset oldAsset = await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId);
            MediaAsset newAsset = await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId);
            Assert.Equal(AssetStatus.READY, oldAsset.Status);
            Assert.Equal(AssetStatus.READY, newAsset.Status);
            Assert.True(newAsset.BindingRevision > oldAsset.BindingRevision);
            return oldAsset.BindingRevision;
        });

        await InvokeMessageAndWaitAsync(new FileAssetDetached(first.AssetId, previousRevision));

        await ExecuteInDb(async db =>
        {
            MediaAsset oldAsset = await db.MediaAssets.SingleAsync(x => x.Id == first.AssetId);
            MediaAsset newAsset = await db.MediaAssets.SingleAsync(x => x.Id == second.AssetId);
            Assert.Equal(AssetStatus.DELETING, oldAsset.Status);
            Assert.Equal(AssetStatus.READY, newAsset.Status);
            Assert.Equal(materialId, newAsset.TargetEntity!.Id);
        });
    }

    [Fact]
    public async Task BindAsset_CrossAuthorSlotCollision_DoesNotDeleteVictimAsset()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse victim =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "victim.png");
        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{victim.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId))))
            .EnsureSuccessStatusCode();

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse attacker =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "attacker.png");
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{attacker.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == victim.AssetId)).Status);
        });
    }

    [Fact]
    public async Task BindAsset_TargetManagerCanReplaceAssetUploadedByAnotherAuthor()
    {
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse previous =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "previous-manager.png");
        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{previous.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId))))
            .EnsureSuccessStatusCode();

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse replacement =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "replacement-manager.png");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{replacement.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        long previousRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == previous.AssetId)).BindingRevision);
        await InvokeMessageAndWaitAsync(new FileAssetDetached(previous.AssetId, previousRevision));
        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.DELETING,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == previous.AssetId)).Status);
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == replacement.AssetId)).Status);
        });
    }

    [Fact]
    public async Task DelayedDetach_DoesNotDeleteAssetSelectedAgainWithNewRevision()
    {
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse first =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "first-selection.png");
        InitiateFileUploadResponse second =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "second-selection.png");
        BindAssetRequest request = new(new TargetEntityDto("material", materialId));

        (await AppHttpClient.PostAsJsonAsync($"/files/{first.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long staleRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == first.AssetId)).BindingRevision);

        (await AppHttpClient.PostAsJsonAsync($"/files/{second.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsJsonAsync($"/files/{first.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();

        await InvokeMessageAndWaitAsync(new FileAssetDetached(first.AssetId, staleRevision));

        await ExecuteInDb(async db =>
        {
            MediaAsset selectedAgain = await db.MediaAssets.SingleAsync(asset => asset.Id == first.AssetId);
            Assert.Equal(AssetStatus.READY, selectedAgain.Status);
            Assert.True(selectedAgain.BindingRevision > staleRevision);
        });
    }

    [Fact]
    public async Task LowerWinningRevision_DetachDeactivatesWithoutDeleting_AndHigherConfirmationReactivates()
    {
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse preview =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "revision-watermark.png");
        BindAssetRequest request = new(new TargetEntityDto("material", materialId));

        (await AppHttpClient.PostAsJsonAsync($"/files/{preview.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long lowerRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);

        (await AppHttpClient.PostAsJsonAsync($"/files/{preview.AssetId}/bind/", request))
            .EnsureSuccessStatusCode();
        long higherRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).BindingRevision);
        Assert.True(higherRevision > lowerRevision);

        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(preview.AssetId, lowerRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(preview.AssetId, lowerRevision));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(item => item.Id == preview.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.Equal(lowerRevision, asset.ConfirmedBindingRevision);
            Assert.Equal(lowerRevision, asset.DetachedThroughBindingRevision);
        });
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IMediaAssetRepository repository = scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();
            Assert.Null(await repository.GetActiveSlotAssetAsync(
                "material",
                materialId,
                AssetUsageType.MATERIAL_PREVIEW));
        }

        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(preview.AssetId, higherRevision));

        await using AsyncServiceScope reactivatedScope = Services.CreateAsyncScope();
        IMediaAssetRepository reactivatedRepository =
            reactivatedScope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();
        MediaAsset? active = await reactivatedRepository.GetActiveSlotAssetAsync(
            "material",
            materialId,
            AssetUsageType.MATERIAL_PREVIEW);
        Assert.Equal(preview.AssetId, active?.Id);
        Assert.Equal(higherRevision, active?.ConfirmedBindingRevision);
    }

    [Fact]
    public async Task BindAssetInternal_ForUncommittedTarget_TrustsEcsButChecksAssetActor()
    {
        Guid actorId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        AuthenticateAs(actorId, "platform-author");
        InitiateFileUploadResponse asset =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "uncommitted-target.png");
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Target ещё не закоммичен"));

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/internal/assets/{asset.AssetId}/bind/",
            new BindAssetInternalRequest(
                new TargetEntityDto("material", materialId),
                actorId,
                ActorCanManageAnyAsset: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await TargetEntityAuthorization.DidNotReceive().AuthorizeAsync(
            Arg.Any<TargetEntity>(),
            Arg.Any<CancellationToken>());
        await ExecuteInDb(async db =>
        {
            MediaAsset stored = await db.MediaAssets.SingleAsync(item => item.Id == asset.AssetId);
            Assert.Equal(materialId, stored.TargetEntity!.Id);
        });
    }

    [Fact]
    public async Task BindAssetInternal_ForForeignAssetActor_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse asset =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "foreign-actor.png");

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/internal/assets/{asset.AssetId}/bind/",
            new BindAssetInternalRequest(
                new TargetEntityDto("material", Guid.NewGuid()),
                Guid.NewGuid(),
                ActorCanManageAnyAsset: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BindAssetInternal_PrivilegedServiceAllowsEmptyActorId()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse asset =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "service-bind.png");

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/internal/assets/{asset.AssetId}/bind/",
            new BindAssetInternalRequest(
                new TargetEntityDto("material", Guid.NewGuid()),
                Guid.Empty,
                ActorCanManageAnyAsset: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DetachAsset_AsOwner_MarksDeleting()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));

        HttpResponseMessage detach = await AppHttpClient.PostAsync(
            $"/files/{preview.AssetId}/detach", content: null);

        Assert.Equal(HttpStatusCode.OK, detach.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == preview.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });
    }

    [Fact]
    public async Task DetachAsset_FormerUploaderWithoutCurrentTargetAccess_Returns403()
    {
        Guid materialId = Guid.NewGuid();
        InitiateFileUploadResponse preview =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid(), "former-manager.png");
        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{preview.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", materialId))))
            .EnsureSuccessStatusCode();
        TargetEntityAuthorization
            .AuthorizeManagerAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/files/{preview.AssetId}/detach",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == preview.AssetId)).Status));
    }

    [Fact]
    public async Task DetachAsset_AlreadyDeleting_ReturnsSuccessNoOp()
    {
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/files/{preview.AssetId}/detach", content: null);
        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/files/{preview.AssetId}/detach", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task DetachAsset_AsNonOwner_Returns403()
    {
        Guid userA = Guid.NewGuid();
        AuthenticateAs(userA, "platform-author");

        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse preview = await InitiateAndCompleteDraftPreviewAsync(draftId, "cover.png");

        Guid userB = Guid.NewGuid();
        AuthenticateAs(userB, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/files/{preview.AssetId}/detach", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("asset.detach.not.owner", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReassignAssetsOwner_ByTargetEntity_AllowsNewAuthorToDetachTransferredMaterialVideo()
    {
        Guid previousAuthorId = Guid.NewGuid();
        Guid newAuthorId = Guid.NewGuid();
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(previousAuthorId, "platform-author");
        InitiateVideoUploadResponse video = await InitiateDraftVideoAsync(draftId, "lesson.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == video.AssetId);
            asset.MarkProcessing();
            await db.SaveChangesAsync();
        });

        HttpResponseMessage bind = await AppHttpClient.PostAsJsonAsync(
            $"/files/{video.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId)));
        Assert.Equal(HttpStatusCode.OK, bind.StatusCode);

        AuthenticateAs(newAuthorId, "platform-author");
        HttpResponseMessage blockedDetach = await AppHttpClient.PostAsync(
            $"/files/{video.AssetId}/detach", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, blockedDetach.StatusCode);

        AuthenticateAsAdmin();
        HttpResponseMessage reassign = await AppHttpClient.PostAsJsonAsync(
            "/internal/assets/reassign-owner",
            new
            {
                NewOwnerId = newAuthorId,
                TargetEntities = new[] { new { Type = "material", Id = materialId } }
            });
        Assert.Equal(HttpStatusCode.OK, reassign.StatusCode);

        AuthenticateAs(newAuthorId, "platform-author");
        HttpResponseMessage detach = await AppHttpClient.PostAsync(
            $"/files/{video.AssetId}/detach", content: null);
        Assert.Equal(HttpStatusCode.OK, detach.StatusCode);
    }

    [Fact]
    public async Task CourseAssetOwnershipChanged_OutOfOrderWithDifferentTargets_AppliesNewestPerTarget()
    {
        Guid originalOwnerId = Guid.NewGuid();
        Guid staleOwnerId = Guid.NewGuid();
        Guid newestOwnerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid removedBeforeRevisionTwoMaterialId = Guid.NewGuid();
        Guid retainedMaterialId = Guid.NewGuid();

        AuthenticateAs(originalOwnerId, "platform-author");
        InitiateVideoUploadResponse removedVideo =
            await InitiateDraftVideoAsync(Guid.NewGuid(), "ownership-removed.mp4");
        InitiateVideoUploadResponse retainedVideo =
            await InitiateDraftVideoAsync(Guid.NewGuid(), "ownership-retained.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset removedAsset = await db.MediaAssets.SingleAsync(x => x.Id == removedVideo.AssetId);
            MediaAsset retainedAsset = await db.MediaAssets.SingleAsync(x => x.Id == retainedVideo.AssetId);
            removedAsset.MarkProcessing();
            retainedAsset.MarkProcessing();
            await db.SaveChangesAsync();
        });

        HttpResponseMessage bindRemoved = await AppHttpClient.PostAsJsonAsync(
            $"/files/{removedVideo.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", removedBeforeRevisionTwoMaterialId)));
        HttpResponseMessage bindRetained = await AppHttpClient.PostAsJsonAsync(
            $"/files/{retainedVideo.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", retainedMaterialId)));
        Assert.Equal(HttpStatusCode.OK, bindRemoved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bindRetained.StatusCode);

        var revisionOneTargets = new[]
        {
            new AssetOwnershipTarget("material", removedBeforeRevisionTwoMaterialId),
            new AssetOwnershipTarget("material", retainedMaterialId),
        };
        var revisionTwoTargets = new[] { new AssetOwnershipTarget("material", retainedMaterialId) };
        await InvokeMessageAndWaitAsync(
            new CourseAssetOwnershipChanged(courseId, 2, newestOwnerId, revisionTwoTargets));
        await InvokeMessageAndWaitAsync(
            new CourseAssetOwnershipChanged(courseId, 1, staleOwnerId, revisionOneTargets));
        await InvokeMessageAndWaitAsync(
            new CourseAssetOwnershipChanged(courseId, 2, newestOwnerId, revisionTwoTargets));

        await ExecuteInDb(async db =>
        {
            MediaAsset removedAsset = await db.MediaAssets.SingleAsync(x => x.Id == removedVideo.AssetId);
            MediaAsset retainedAsset = await db.MediaAssets.SingleAsync(x => x.Id == retainedVideo.AssetId);
            List<AssetOwnershipCheckpoint> checkpoints = await db.AssetOwnershipCheckpoints
                .Where(x => x.CourseId == courseId)
                .ToListAsync();

            Assert.Equal(staleOwnerId, removedAsset.UploadedByUserId);
            Assert.Equal(newestOwnerId, retainedAsset.UploadedByUserId);
            Assert.Contains(checkpoints, x =>
                x.TargetType == "material"
                && x.TargetId == removedBeforeRevisionTwoMaterialId
                && x.LastAppliedRevision == 1);
            Assert.Contains(checkpoints, x =>
                x.TargetType == "material"
                && x.TargetId == retainedMaterialId
                && x.LastAppliedRevision == 2);
        });
    }

    [Fact]
    public async Task CourseAssetOwnershipChanged_AssetCommittedAfterProjection_UsesDesiredOwner()
    {
        Guid staleOwnerId = Guid.NewGuid();
        Guid desiredOwnerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new CourseAssetOwnershipChanged(
            courseId,
            1,
            desiredOwnerId,
            [new AssetOwnershipTarget("material", materialId)]));

        AuthenticateAs(staleOwnerId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/videos/uploads/",
            new InitiateVideoUploadRequest(
                "late-after-transfer.mp4",
                "video/mp4",
                1024,
                "material_video",
                new TargetEntityDto("material", materialId)));
        response.EnsureSuccessStatusCode();
        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(
                candidate => candidate.Id == envelope!.Result!.AssetId);
            AssetOwnershipCheckpoint checkpoint = await db.AssetOwnershipCheckpoints.SingleAsync(
                candidate => candidate.CourseId == courseId
                             && candidate.TargetType == "material"
                             && candidate.TargetId == materialId);

            Assert.Equal(desiredOwnerId, checkpoint.DesiredOwnerId);
            Assert.Equal(desiredOwnerId, asset.UploadedByUserId);
        });
    }

    [Fact]
    public async Task CourseAssetOwnershipChanged_TargetMovedAcrossCourses_IgnoresOlderSourceCourseEvent()
    {
        Guid originalOwnerId = Guid.NewGuid();
        Guid staleOwnerId = Guid.NewGuid();
        Guid newestOwnerId = Guid.NewGuid();
        Guid sourceCourseId = Guid.NewGuid();
        Guid targetCourseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(originalOwnerId, "platform-author");
        InitiateVideoUploadResponse video =
            await InitiateDraftVideoAsync(Guid.NewGuid(), "cross-course-owner.mp4");
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == video.AssetId);
            asset.MarkProcessing();
            await db.SaveChangesAsync();
        });
        (await AppHttpClient.PostAsJsonAsync(
            $"/files/{video.AssetId}/bind",
            new BindAssetRequest(new TargetEntityDto("material", materialId))))
            .EnsureSuccessStatusCode();

        AssetOwnershipTarget[] target = [new("material", materialId)];
        await InvokeMessageAndWaitAsync(new CourseAssetOwnershipChanged(
            targetCourseId,
            2,
            newestOwnerId,
            target));
        await InvokeMessageAndWaitAsync(new CourseAssetOwnershipChanged(
            sourceCourseId,
            1,
            staleOwnerId,
            target));

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == video.AssetId);
            List<AssetOwnershipCheckpoint> checkpoints = await db.AssetOwnershipCheckpoints
                .Where(x => x.TargetType == "material" && x.TargetId == materialId)
                .ToListAsync();

            Assert.Equal(newestOwnerId, asset.UploadedByUserId);
            AssetOwnershipCheckpoint checkpoint = Assert.Single(checkpoints);
            Assert.Equal(targetCourseId, checkpoint.CourseId);
            Assert.Equal(2, checkpoint.LastAppliedRevision);
        });
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteDraftPreviewAsync(Guid draftId, string fileName)
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: fileName,
                ContentType: "image/png",
                Size: 4,
                UsageType: "material_preview",
                DraftId: draftId,
                TargetEntity: null));

        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        completeResponse.EnsureSuccessStatusCode();
        return initiateResult;
    }

    private async Task<InitiateVideoUploadResponse> InitiateDraftVideoAsync(Guid draftId, string fileName)
    {
        var request = new InitiateVideoUploadRequest(
            FileName: fileName,
            ContentType: "video/mp4",
            Size: 1024,
            UsageType: "material_video",
            TargetEntity: null,
            DraftId: draftId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/videos/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateVideoUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateVideoUploadResponse>>();
        return envelope!.Result!;
    }

    private async Task<InitiateFileUploadResponse> InitiateFileUploadAsync(InitiateFileUploadRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        response.EnsureSuccessStatusCode();

        SharedKernel.Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<SharedKernel.Envelope<InitiateFileUploadResponse>>();
        return envelope!.Result!;
    }

    private async Task UploadToDirectUrlAsync(string url, IReadOnlyDictionary<string, string> headers, byte[] data)
    {
        using var content = new ByteArrayContent(data);
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(header.Value);
            }
            else
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        HttpResponseMessage response = await HttpClient.PutAsync(url, content);
        response.EnsureSuccessStatusCode();
    }
}
