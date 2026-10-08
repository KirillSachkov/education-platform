using System.Net;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TargetEntityAuthorizationTests : FileServiceTestsBase
{
    public TargetEntityAuthorizationTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task InitiateVideoUpload_ForForeignMaterial_Returns403()
    {
        AuthenticateAsAdmin();
        DenyTargetAccess();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/videos/uploads/",
            new InitiateVideoUploadRequest(
                "foreign.mp4",
                "video/mp4",
                1024,
                "material_video",
                new TargetEntityDto("material", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<VideoUploadInitiated>());
    }

    [Fact]
    public async Task InitiateFileUpload_ForForeignCourse_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        DenyTargetAccess();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/files/uploads/",
            new InitiateFileUploadRequest(
                "foreign.png",
                "image/png",
                4,
                "course_preview",
                DraftId: null,
                TargetEntity: new TargetEntityDto("course", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttachExistingVideo_ToForeignMaterial_Returns403()
    {
        AuthenticateAsAdmin();
        DenyTargetAccess();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/videos/attach-existing/",
            new AttachExistingVideoRequest(
                $"foreign-{Guid.NewGuid():N}",
                "material_video",
                new TargetEntityDto("material", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<VideoReadyForProcessing>());
    }

    [Fact]
    public async Task BindDraftAssets_ToForeignMaterial_Returns403WithoutProcessingEvent()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse asset = await InitiateAndCompleteDraftPreviewAsync(draftId);
        DenyTargetAccess();
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind/",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", Guid.NewGuid()),
                [asset.AssetId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
        Assert.Empty(OutboxCollector.OfType<VideoReadyForProcessing>());
    }

    [Fact]
    public async Task BindAsset_ToForeignMaterial_Returns403WithoutBoundEvent()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        InitiateFileUploadResponse asset =
            await InitiateAndCompleteDraftPreviewAsync(Guid.NewGuid());
        DenyTargetAccess();
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{asset.AssetId}/bind/",
            new BindAssetRequest(new TargetEntityDto("material", Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<FileAssetBound>());
    }

    [Fact]
    public async Task SyncEntityAssets_ForForeignMaterial_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        DenyTargetAccess();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assets/sync/",
            new SyncEntityAssetsRequest(
                new TargetEntityDto("material", Guid.NewGuid()),
                ["markdown_image"],
                []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullEntityQueries_ForForeignMaterial_Return403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        DenyTargetAccess();
        Guid materialId = Guid.NewGuid();

        HttpResponseMessage videoResponse = await AppHttpClient.GetAsync(
            $"/videos/by-entity?entityId={materialId}&entityType=material");
        HttpResponseMessage filesResponse = await AppHttpClient.GetAsync(
            $"/files/by-entity?entityId={materialId}&entityType=material");

        Assert.Equal(HttpStatusCode.Forbidden, videoResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, filesResponse.StatusCode);
    }

    private void DenyTargetAccess()
    {
        Error error = Error.Authorization("target.not.owner", "Нет доступа к целевой сущности");
        TargetEntityAuthorization
            .AuthorizeAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(error);
        TargetEntityAuthorization
            .AuthorizeManagerAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(error);
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteDraftPreviewAsync(Guid draftId)
    {
        HttpResponseMessage initiateResponse = await AppHttpClient.PostAsJsonAsync(
            "/files/uploads/",
            new InitiateFileUploadRequest(
                "draft.png",
                "image/png",
                4,
                "material_preview",
                DraftId: draftId,
                TargetEntity: null));
        initiateResponse.EnsureSuccessStatusCode();
        Envelope<InitiateFileUploadResponse>? initiate =
            await initiateResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        using var content = new ByteArrayContent([1, 2, 3, 4]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        HttpResponseMessage upload = await HttpClient.PutAsync(initiate!.Result!.UploadUrl, content);
        upload.EnsureSuccessStatusCode();

        HttpResponseMessage complete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiate.Result.AssetId}/complete/",
            new CompleteFileUploadRequest(null));
        complete.EnsureSuccessStatusCode();

        return initiate.Result;
    }
}
