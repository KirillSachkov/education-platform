using System.Net;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.IntegrationTests.Infrastructure;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AssetRegistryAuthorizationTests : FileServiceTestsBase
{
    public AssetRegistryAuthorizationTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── POST /assets/sync ────────────────────────────────────────────────
    [Fact]
    public async Task SyncEntityAssets_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        SyncEntityAssetsRequest request = new(
            new TargetEntityDto("material", Guid.NewGuid()),
            ["markdown_image"],
            [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncEntityAssets_UserWithoutManagePermission_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        SyncEntityAssetsRequest request = new(
            new TargetEntityDto("material", Guid.NewGuid()),
            ["markdown_image"],
            [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── POST /draft-assets/bind ──────────────────────────────────────────
    [Fact]
    public async Task BindDraftAssets_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        BindDraftAssetsRequest request = new(
            Guid.NewGuid(),
            new TargetEntityDto("material", Guid.NewGuid()),
            [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/draft-assets/bind", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BindDraftAssets_UserWithoutManagePermission_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        BindDraftAssetsRequest request = new(
            Guid.NewGuid(),
            new TargetEntityDto("material", Guid.NewGuid()),
            [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/draft-assets/bind", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
