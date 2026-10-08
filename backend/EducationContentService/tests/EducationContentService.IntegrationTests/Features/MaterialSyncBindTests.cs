using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.IntegrationTests.Infrastructure;
using EducationContentService.Infrastructure.Postgres;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialSyncBindTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public MaterialSyncBindTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateMaterial_WithVideoIdAndPreviewId_BindsAndAttaches()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid videoAssetId = Guid.CreateVersion7();
        Guid previewAssetId = Guid.CreateVersion7();

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new CreateMaterialRequest(
            "Материал с медиа",
            "# Содержимое",
            MaterialKind.VIDEO.ToString(),
            AccessType.PUBLIC.ToString(),
            DraftId: null,
            VideoId: videoAssetId,
            PreviewId: previewAssetId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);
        response.EnsureSuccessStatusCode();

        await fileClient.Received(1).BindAssetInternalAsync(
            videoAssetId,
            Arg.Is<BindAssetInternalRequest>(r =>
                r.TargetEntity.Type == "material" &&
                r.ActorUserId == authorId &&
                !r.ActorCanManageAnyAsset),
            Arg.Any<CancellationToken>());
        await fileClient.Received(1).BindAssetInternalAsync(
            previewAssetId,
            Arg.Is<BindAssetInternalRequest>(r =>
                r.TargetEntity.Type == "material" &&
                r.ActorUserId == authorId &&
                !r.ActorCanManageAnyAsset),
            Arg.Any<CancellationToken>());

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(ct);
            Assert.NotNull(material.VideoId);
            Assert.Equal(videoAssetId, material.VideoId!.Value);
            Assert.NotNull(material.ImageId);
            Assert.Equal(previewAssetId, material.ImageId!.Value);
        });
    }

    [Fact]
    public async Task CreateMaterial_WhenBindFails_DoesNotPersistMaterial()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid videoAssetId = Guid.CreateVersion7();
        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        try
        {
            fileClient.BindAssetInternalAsync(
                    videoAssetId,
                    Arg.Any<BindAssetInternalRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Failure<BindAssetResponse, Error>(
                    Error.Conflict("asset.already.bound", "уже привязан")));

            var request = new CreateMaterialRequest(
                "Материал с упавшим bind",
                "# Содержимое",
                MaterialKind.VIDEO.ToString(),
                AccessType.PUBLIC.ToString(),
                DraftId: null,
                VideoId: videoAssetId);

            HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

            Assert.False(response.IsSuccessStatusCode);

            await ExecuteInDb(async db =>
            {
                Assert.Equal(0, await db.Materials.CountAsync(ct));
            });
        }
        finally
        {
            // Restore default success behaviour even if Asserts above throw,
            // otherwise siblings in the same xUnit collection see the failing mock.
            fileClient.BindAssetInternalAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<BindAssetInternalRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<BindAssetResponse, Error>(new BindAssetResponse(1)));
        }
    }

    [Fact]
    public async Task UpdateMaterial_AttachesNewVideo_CallsBindOnce()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid materialId = await CreateBareMaterialAsync(authorId, ct);
        Guid newVideoId = Guid.CreateVersion7();

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new UpdateMaterialRequest(
            "Обновлённый материал",
            "# Контент",
            MaterialKind.VIDEO.ToString(),
            AccessType.PUBLIC.ToString(),
            VideoId: newVideoId);

        HttpResponseMessage response = await PatchAsJsonAsync($"/materials/{materialId}", request);
        response.EnsureSuccessStatusCode();

        await fileClient.Received(1).BindAssetInternalAsync(
            newVideoId,
            Arg.Any<BindAssetInternalRequest>(),
            Arg.Any<CancellationToken>());
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(m => m.Id == materialId, ct);
            Assert.NotNull(material.VideoId);
            Assert.Equal(newVideoId, material.VideoId!.Value);
            Assert.True(material.VideoBindingRevision > 0);
        });
    }

    [Fact]
    public async Task UpdateMaterial_DetachesVideo_WhenVideoIdSetToNull()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid existingVideoId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(authorId, ct, withVideoId: existingVideoId);

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new UpdateMaterialRequest(
            "Без видео",
            "# Только текст",
            MaterialKind.ARTICLE.ToString(),
            AccessType.PUBLIC.ToString(),
            VideoId: null);

        HttpResponseMessage response = await PatchAsJsonAsync($"/materials/{materialId}", request);
        response.EnsureSuccessStatusCode();

        FileAssetDetached detached = Assert.Single(_factory.OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(existingVideoId, detached.AssetId);
        Assert.Equal(0, detached.ExpectedBindingRevision);
        await fileClient.DidNotReceive().BindAssetInternalAsync(
            Arg.Any<Guid>(), Arg.Any<BindAssetInternalRequest>(), Arg.Any<CancellationToken>());

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(m => m.Id == materialId, ct);
            Assert.Null(material.VideoId);
        });
    }

    [Fact]
    public async Task UpdatePublishedMaterial_RemovingLastPayload_Returns400BeforeFileCall()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(authorId, ct, withVideoId: videoId);
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(item => item.Id == materialId, ct);
            Assert.True(material.Publish().IsSuccess);
            await db.SaveChangesAsync(ct);
        });
        AuthenticateAs(authorId, "platform-author");

        IFileServiceClient fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/materials/{materialId}",
            new UpdateMaterialRequest(
                "Пустой опубликованный материал",
                Content: null,
                Kind: MaterialKind.VIDEO.ToString(),
                AccessType: AccessType.PUBLIC.ToString(),
                VideoId: null));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        await fileClient.DidNotReceive().BindAssetInternalAsync(
            Arg.Any<Guid>(), Arg.Any<BindAssetInternalRequest>(), Arg.Any<CancellationToken>());
        Assert.Empty(_factory.OutboxCollector.OfType<FileAssetDetached>());
        await ExecuteInDb(async db =>
        {
            Material persisted = await db.Materials.AsNoTracking().SingleAsync(item => item.Id == materialId, ct);
            Assert.Equal(PublicationStatus.PUBLISHED, persisted.Status);
            Assert.Equal(videoId, persisted.VideoId!.Value);
        });
    }

    [Fact]
    public async Task UpdateMaterial_SameVideoId_NoBindOrDetach()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid videoId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(authorId, ct, withVideoId: videoId);

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        var request = new UpdateMaterialRequest(
            "Тот же материал",
            "# Содержимое",
            MaterialKind.VIDEO.ToString(),
            AccessType.PUBLIC.ToString(),
            VideoId: videoId);

        HttpResponseMessage response = await PatchAsJsonAsync($"/materials/{materialId}", request);
        response.EnsureSuccessStatusCode();

        await fileClient.DidNotReceive().BindAssetInternalAsync(
            Arg.Any<Guid>(), Arg.Any<BindAssetInternalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMaterial_WhenSecondBindFails_KeepsPreviousMediaWithoutUnsafeCompensation()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid oldVideoId = Guid.CreateVersion7();
        Guid oldPreviewId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(
            authorId,
            ct,
            withVideoId: oldVideoId,
            withPreviewId: oldPreviewId);
        Guid newVideoId = Guid.CreateVersion7();
        Guid newPreviewId = Guid.CreateVersion7();

        IFileServiceClient fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();
        try
        {
            fileClient.BindAssetInternalAsync(
                    newPreviewId,
                    Arg.Any<BindAssetInternalRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Failure<BindAssetResponse, Error>(
                    Error.Failure("file.bind.failed", "second bind failed")));

            var request = new UpdateMaterialRequest(
                "Неуспешная замена",
                "# Контент",
                MaterialKind.VIDEO.ToString(),
                AccessType.PUBLIC.ToString(),
                VideoId: newVideoId,
                PreviewId: newPreviewId);

            HttpResponseMessage response = await PatchAsJsonAsync($"/materials/{materialId}", request);
            Assert.False(response.IsSuccessStatusCode);

            await ExecuteInDb(async db =>
            {
                Material material = await db.Materials.SingleAsync(item => item.Id == materialId, ct);
                Assert.Equal(oldVideoId, material.VideoId!.Value);
                Assert.Equal(oldPreviewId, material.ImageId!.Value);
            });

            await fileClient.DidNotReceive().DetachAssetAsync(
                Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            fileClient.BindAssetInternalAsync(
                    newPreviewId,
                    Arg.Any<BindAssetInternalRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result.Success<BindAssetResponse, Error>(new BindAssetResponse(1)));
        }
    }

    [Fact]
    public async Task ConcurrentMediaUpdates_RejectStaleAggregateWriter()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(authorId, ct);

        await using AsyncServiceScope firstScope = Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = Services.CreateAsyncScope();
        EducationDbContext firstDb = firstScope.ServiceProvider.GetRequiredService<EducationDbContext>();
        EducationDbContext secondDb = secondScope.ServiceProvider.GetRequiredService<EducationDbContext>();
        Material first = await firstDb.Materials.SingleAsync(item => item.Id == materialId, ct);
        Material stale = await secondDb.Materials.SingleAsync(item => item.Id == materialId, ct);

        Guid firstVideoId = Guid.CreateVersion7();
        Guid staleVideoId = Guid.CreateVersion7();
        first.AttachVideo(
            EducationContentService.Domain.ValueObjects.VideoId.Create(firstVideoId).Value,
            bindingRevision: 100);
        stale.AttachVideo(
            EducationContentService.Domain.ValueObjects.VideoId.Create(staleVideoId).Value,
            bindingRevision: 101);

        await firstDb.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(ct));

        await ExecuteInDb(async db =>
        {
            Material persisted = await db.Materials.SingleAsync(item => item.Id == materialId, ct);
            Assert.Equal(firstVideoId, persisted.VideoId!.Value);
            Assert.Equal(100, persisted.VideoBindingRevision);
        });
    }

    [Fact]
    public async Task VerifyFileAssetBinding_MatchingAggregateConfirmsAuthoritativeRevision()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(Guid.CreateVersion7(), CancellationToken.None);
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(item => item.Id == materialId);
            material.AttachVideo(
                EducationContentService.Domain.ValueObjects.VideoId.Create(videoId).Value,
                bindingRevision: 42);
            await db.SaveChangesAsync();
        });
        _factory.OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VerifyFileAssetBindingHandler handler =
            ActivatorUtilities.CreateInstance<VerifyFileAssetBindingHandler>(scope.ServiceProvider);
        await handler.Handle(
            new VerifyFileAssetBinding(videoId, "material_video", materialId, "material", 99),
            CancellationToken.None);

        FileAssetBindingConfirmed confirmed =
            Assert.Single(_factory.OutboxCollector.OfType<FileAssetBindingConfirmed>());
        Assert.Equal(videoId, confirmed.AssetId);
        Assert.Equal(42, confirmed.BindingRevision);
        Assert.Empty(_factory.OutboxCollector.OfType<FileAssetDetached>());
    }

    [Fact]
    public async Task VerifyFileAssetBinding_AbandonedCandidatePublishesRevisionMatchedDetach()
    {
        Guid authoritativeVideoId = Guid.CreateVersion7();
        Guid abandonedVideoId = Guid.CreateVersion7();
        Guid materialId = await CreateBareMaterialAsync(Guid.CreateVersion7(), CancellationToken.None);
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(item => item.Id == materialId);
            material.AttachVideo(
                EducationContentService.Domain.ValueObjects.VideoId.Create(authoritativeVideoId).Value,
                bindingRevision: 42);
            await db.SaveChangesAsync();
        });
        _factory.OutboxCollector.Clear();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        VerifyFileAssetBindingHandler handler =
            ActivatorUtilities.CreateInstance<VerifyFileAssetBindingHandler>(scope.ServiceProvider);
        await handler.Handle(
            new VerifyFileAssetBinding(abandonedVideoId, "material_video", materialId, "material", 99),
            CancellationToken.None);

        FileAssetDetached detached =
            Assert.Single(_factory.OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(abandonedVideoId, detached.AssetId);
        Assert.Equal(99, detached.ExpectedBindingRevision);
        Assert.Empty(_factory.OutboxCollector.OfType<FileAssetBindingConfirmed>());
    }

    private async Task<Guid> CreateBareMaterialAsync(
        Guid authorId,
        CancellationToken ct,
        Guid? withVideoId = null,
        Guid? withPreviewId = null)
    {
        // Direct DB seed avoids dragging the Create endpoint through every test —
        // these tests target the Update path's bind/detach diff logic.
        Material material = new(
            authorId,
            EducationContentService.Domain.ValueObjects.Title.Create($"Материал {Guid.NewGuid():N}").Value,
            MaterialKind.ARTICLE,
            AccessType.PUBLIC);

        if (withVideoId is { } videoId)
        {
            material.AttachVideo(EducationContentService.Domain.ValueObjects.VideoId.Create(videoId).Value);
        }


        if (withPreviewId is { } previewId)
        {
            material.AttachImage(EducationContentService.Domain.ValueObjects.ImageId.Create(previewId).Value);
        }

        await ExecuteInDb(async db =>
        {
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
        });

        return material.Id;
    }
}
