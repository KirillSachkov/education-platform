using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class FileAuthorizationTests : FileServiceTestsBase
{
    public FileAuthorizationTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task InitiateFileUpload_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        InitiateFileUploadRequest request = new(
            FileName: "preview.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InitiateFileUpload_UserWithoutUploadPermission_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "no-permissions-role");

        InitiateFileUploadRequest request = new(
            FileName: "preview.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InitiateAvatarUpload_ForAnotherUser_Returns403()
    {
        Guid currentUserId = Guid.NewGuid();
        AuthenticateAs(currentUserId, "platform-participant");

        InitiateFileUploadRequest request = new(
            FileName: "avatar.jpg",
            ContentType: "image/jpeg",
            Size: 4,
            UsageType: "avatar",
            DraftId: null,
            TargetEntity: new TargetEntityDto("user", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads/", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/files/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_NonAdminUser_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/files/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_FormerUploaderWithoutCurrentTargetAccess_Returns403()
    {
        Guid ownerId = Guid.NewGuid();
        Guid assetId = await CreateReadyCoursePreviewAssetAsync(ownerId);
        AuthenticateAs(ownerId, "platform-author");
        TargetEntityAuthorization
            .AuthorizeManagerAsync(Arg.Any<TargetEntity>(), Arg.Any<CancellationToken>())
            .Returns(Error.Authorization("target.not.owner", "Нет доступа к целевой сущности"));

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/files/{assetId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == assetId)).Status));
    }

    [Fact]
    public async Task GetFileContent_Anonymous_ReturnsNotFound_ForNonExistentFile()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/files/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_Anonymous_ReturnsForbidden_ForProtectedMarkdownAsset()
    {
        Guid assetId = await CreateProtectedMarkdownAssetAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/files/{assetId}/content");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_Anonymous_ReturnsForbidden_ForReadyDraftMarkdownAsset()
    {
        Guid assetId = await CreateReadyDraftMarkdownAssetAsync(Guid.CreateVersion7());
        using HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        noRedirectClient.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage response = await noRedirectClient.GetAsync($"/files/{assetId}/content");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("plan_onboarding_step")]
    public async Task GetFileContent_Anonymous_ReturnsForbidden_ForOwnerOnlyMarkdownTargets(string targetType)
    {
        Guid assetId = await CreateProtectedMarkdownAssetAsync(targetType);
        using HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        noRedirectClient.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage response = await noRedirectClient.GetAsync($"/files/{assetId}/content");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_PublicCoursePreview_Anonymous_ShouldReturn302()
    {
        // COURSE_PREVIEW is non-protected (not in _protectedUsageTypes of
        // GetFileContentEndpoint). Anonymous callers must be redirected to the
        // presigned MinIO URL, not rejected. Regression guard for public catalog
        // previews being accidentally gated by the entitlement check.
        Guid assetId = await CreateReadyCoursePreviewAssetAsync();

        using HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        noRedirectClient.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage response = await noRedirectClient.GetAsync($"/files/{assetId}/content");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task GetFile_ForeignAuthorIsDenied_ButInternalServiceCanReadReadyFile()
    {
        Guid ownerId = Guid.NewGuid();
        Guid assetId = await CreateReadyCoursePreviewAssetAsync(ownerId);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage publicResponse = await AppHttpClient.GetAsync($"/files/{assetId}");
        Assert.Equal(HttpStatusCode.Forbidden, publicResponse.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-service");
        HttpResponseMessage internalResponse = await AppHttpClient.GetAsync($"/internal/files/{assetId}/");
        Assert.Equal(HttpStatusCode.OK, internalResponse.StatusCode);
        Envelope<GetFileResponse?>? envelope =
            await internalResponse.Content.ReadFromJsonAsync<Envelope<GetFileResponse?>>();
        Assert.NotNull(envelope?.Result);
    }

    [Fact]
    public async Task GetFileContent_Admin_ReturnsRedirect_ForProtectedMarkdownAsset()
    {
        Guid assetId = await CreateProtectedMarkdownAssetAsync();

        using HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        HttpResponseMessage response = await noRedirectClient.GetAsync($"/files/{assetId}/content");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task GetFileContent_WithWidth_Anonymous_ReturnsForbidden_ForProtectedMarkdownAsset()
    {
        // #646 regression guard: the responsive `?w=` path must run the SAME entitlement
        // check as the plain content path. A denied caller must get 403, NOT a 302 to the
        // (smaller) variant — otherwise variants would be a public bypass of the lock.
        // The asset carries a generated variant so the request resolves to one if it leaked.
        Guid assetId = await CreateProtectedMarkdownAssetWithVariantsAsync();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/files/{assetId}/content?w=320");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetFileContent_WithWidth_NonEnrolledUser_ReturnsForbidden_ForProtectedMarkdownAsset()
    {
        // Authenticated-but-not-entitled caller. With no Redis access tags the resource is
        // fail-closed (RESOURCE_NOT_REGISTERED) → the `?w=` variant path must still 403,
        // not redirect to the variant object. Distinct user from any owner.
        Guid assetId = await CreateProtectedMarkdownAssetWithVariantsAsync();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/files/{assetId}/content?w=320");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelFileUpload_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/files/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteFileUpload_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{Guid.NewGuid()}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteFileUpload_NonOwnerWithoutFilesManage_Returns403()
    {
        // Arrange — user A (PARTICIPANT: has files.upload but no files.manage) initiates an upload
        Guid userA = Guid.NewGuid();
        AuthenticateAs(userA, "platform-participant");

        InitiateFileUploadRequest request = new(
            FileName: "note.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "markdown_image",
            DraftId: Guid.NewGuid(),
            TargetEntity: null);

        HttpResponseMessage initiateResponse = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        initiateResponse.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await initiateResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();
        Assert.NotNull(envelope?.Result);
        Guid assetId = envelope.Result.AssetId;

        // Act — user B (also PARTICIPANT, lacks files.manage) tries to complete user A's upload
        Guid userB = Guid.NewGuid();
        AuthenticateAs(userB, "platform-participant");

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{assetId}/complete",
            new CompleteFileUploadRequest(null));

        // Assert — 403 Forbidden (ownership check)
        Assert.Equal(HttpStatusCode.Forbidden, completeResponse.StatusCode);
    }

    [Fact]
    public async Task CancelFileUpload_NonOwnerWithoutFilesManage_Returns403()
    {
        // Arrange — user A (PARTICIPANT: has files.upload but no files.manage) initiates an upload
        Guid userA = Guid.NewGuid();
        AuthenticateAs(userA, "platform-participant");

        InitiateFileUploadRequest request = new(
            FileName: "note.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "markdown_image",
            DraftId: Guid.NewGuid(),
            TargetEntity: null);

        HttpResponseMessage initiateResponse = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        initiateResponse.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await initiateResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();
        Assert.NotNull(envelope?.Result);
        Guid assetId = envelope.Result.AssetId;

        // Act — user B (also PARTICIPANT, lacks files.manage) tries to cancel user A's upload
        Guid userB = Guid.NewGuid();
        AuthenticateAs(userB, "platform-participant");

        HttpResponseMessage cancelResponse = await AppHttpClient.PostAsync($"/files/{assetId}/cancel", null);

        // Assert — 403 Forbidden (ownership check)
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CompleteFileUpload_NonOwnerAuthor_Returns403()
    {
        Guid assetId = await InitiateMarkdownImageAsync(Guid.NewGuid());
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{assetId}/complete/",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelFileUpload_NonOwnerAuthor_Returns403()
    {
        Guid assetId = await InitiateMarkdownImageAsync(Guid.NewGuid());
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/files/{assetId}/cancel/", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> InitiateMarkdownImageAsync(Guid ownerId)
    {
        AuthenticateAs(ownerId, "platform-participant");

        InitiateFileUploadRequest request = new(
            FileName: "note.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "markdown_image",
            DraftId: Guid.NewGuid(),
            TargetEntity: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads/", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();
        return envelope!.Result!.AssetId;
    }

    private async Task<Guid> CreateReadyCoursePreviewAssetAsync(Guid? ownerId = null)
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Guid courseId = Guid.NewGuid();

            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of("course", courseId);
            Result<FileName, Error> fileNameResult = FileName.Of($"preview-{Guid.NewGuid():N}.png");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("image/png");

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                AssetUsageType.COURSE_PREVIEW,
                fileNameResult.Value,
                contentTypeResult.Value,
                4,
                null,
                targetEntityResult.Value,
                false,
                ownerId);

            MediaAsset asset = assetResult.Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;

            asset.MarkReady();

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;

            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> CreateProtectedMarkdownAssetWithVariantsAsync()
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Guid materialId = Guid.NewGuid();

            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of("material", materialId);
            Result<FileName, Error> fileNameResult = FileName.Of($"asset-{Guid.NewGuid():N}.png");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("image/png");

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                AssetUsageType.MARKDOWN_IMAGE,
                fileNameResult.Value,
                contentTypeResult.Value,
                4,
                Guid.NewGuid(),
                null,
                true);

            MediaAsset asset = assetResult.Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;

            asset.MarkReady();
            asset.BindTo(targetEntityResult.Value);

            // Variant present → if the entitlement check were skipped, the `?w=320` path would
            // 302 to this variant. We assert it 403s instead, proving the gate runs first.
            asset.SetImageVariants(
            [
                new ImageVariant(320, $"files/variants/{asset.Id:N}_320.webp", "image/webp", 1000),
            ]);

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;

            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> CreateProtectedMarkdownAssetAsync(string targetType = "material")
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Guid materialId = Guid.NewGuid();

            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of(targetType, materialId);
            Result<FileName, Error> fileNameResult = FileName.Of($"asset-{Guid.NewGuid():N}.png");
            Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("image/png");

            Result<MediaAsset, Error> assetResult = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                AssetUsageType.MARKDOWN_IMAGE,
                fileNameResult.Value,
                contentTypeResult.Value,
                4,
                Guid.NewGuid(),
                null,
                true);

            MediaAsset asset = assetResult.Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;

            asset.MarkReady();
            asset.BindTo(targetEntityResult.Value);

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;

            await db.SaveChangesAsync();
        });

        return assetId;
    }

    private async Task<Guid> CreateReadyDraftMarkdownAssetAsync(Guid ownerId)
    {
        Guid assetId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = MediaAsset.Register(
                Guid.CreateVersion7(),
                AssetKind.FILE,
                AssetUsageType.MARKDOWN_IMAGE,
                FileName.Of($"draft-{Guid.CreateVersion7():N}.png").Value,
                MediaContentType.Of("image/png").Value,
                4,
                Guid.CreateVersion7(),
                null,
                true,
                ownerId).Value;
            FileStorageRef storageRef =
                FileStorageRef.Create(asset.Id, StorageKey.ForFile(asset.Id, "png").Value).Value;
            asset.MarkReady();

            db.MediaAssets.Add(asset);
            db.FileStorageRefs.Add(storageRef);
            assetId = asset.Id;
            await db.SaveChangesAsync();
        });

        return assetId;
    }
}
