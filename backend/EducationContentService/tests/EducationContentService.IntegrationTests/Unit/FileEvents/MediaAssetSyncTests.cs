using CSharpFunctionalExtensions;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.FileEvents;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Contracts.HttpCommunication;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit.FileEvents;

public sealed class MediaAssetSyncTests
{
    [Fact]
    public void BindingVerificationPolicy_UsesShortBindLeaseAndLongUploadRetention()
    {
        FileAssetBound prepared = new(
            Guid.CreateVersion7(), "video", "material_video", Guid.CreateVersion7(), "material", 10, true);
        FileAssetBound uploaded = prepared with { RequiresAuthoritativeConfirmation = false };
        FileAssetBound markdown = prepared with
        {
            UsageType = "markdown_image",
            RequiresAuthoritativeConfirmation = false,
        };

        Assert.Equal(BindingVerificationPolicy.SynchronousBindDelay, BindingVerificationPolicy.GetDelay(prepared));
        Assert.Equal(BindingVerificationPolicy.UploadCandidateDelay, BindingVerificationPolicy.GetDelay(uploaded));
        Assert.Null(BindingVerificationPolicy.GetDelay(markdown));
    }

    [Fact]
    public async Task IdenticalConcurrentSelection_UsesSameIdempotencyKey()
    {
        Guid previous = Guid.CreateVersion7();
        Guid requested = Guid.CreateVersion7();
        TargetEntityDto target = new("material", Guid.CreateVersion7());
        var requests = new List<BindAssetInternalRequest>();
        IFileServiceClient client = CreateClient(requests);
        IOutboxService outbox = Substitute.For<IOutboxService>();

        await SyncAsync(client, outbox, previous, previousRevision: 41, requested, target);
        await SyncAsync(client, outbox, previous, previousRevision: 41, requested, target);

        Assert.Equal(2, requests.Count);
        Assert.NotNull(requests[0].SelectionId);
        Assert.Equal(requests[0].SelectionId, requests[1].SelectionId);
    }

    [Fact]
    public async Task NewAuthoritativeTransition_UsesDifferentIdempotencyKey()
    {
        Guid previous = Guid.CreateVersion7();
        Guid requested = Guid.CreateVersion7();
        TargetEntityDto target = new("material", Guid.CreateVersion7());
        var requests = new List<BindAssetInternalRequest>();
        IFileServiceClient client = CreateClient(requests);
        IOutboxService outbox = Substitute.For<IOutboxService>();

        await SyncAsync(client, outbox, previous, previousRevision: 41, requested, target);
        await SyncAsync(client, outbox, previous, previousRevision: 42, requested, target);

        Assert.Equal(2, requests.Count);
        Assert.NotEqual(requests[0].SelectionId, requests[1].SelectionId);
    }

    private static IFileServiceClient CreateClient(List<BindAssetInternalRequest> requests)
    {
        IFileServiceClient client = Substitute.For<IFileServiceClient>();
        client.BindAssetInternalAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindAssetInternalRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.ArgAt<BindAssetInternalRequest>(1));
                return Result.Success<BindAssetResponse, Error>(new BindAssetResponse(100));
            });
        return client;
    }

    private static async Task SyncAsync(
        IFileServiceClient client,
        IOutboxService outbox,
        Guid previous,
        long previousRevision,
        Guid requested,
        TargetEntityDto target)
    {
        UnitResult<Error> result = await MediaAssetSync.SyncSingleAssetAsync(
            client,
            outbox,
            previous,
            previousRevision,
            requested,
            target,
            actorUserId: Guid.CreateVersion7(),
            actorCanManageAnyAsset: false,
            attach: (_, _) => { },
            detach: () => { },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
