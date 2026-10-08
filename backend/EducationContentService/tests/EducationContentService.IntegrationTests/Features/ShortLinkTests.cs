using System.Net;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.ShortLinks;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ShortLinks;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class ShortLinkTests : EducationContentServiceTestsBase
{
    private readonly HttpClient _noRedirectClient;

    public ShortLinkTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        // Клиент с AllowAutoRedirect = false, чтобы видеть сам 302 (иначе HttpClient улетит
        // за Location и тест увидит финальный статус вместо редиректа).
        _noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task GetOrCreateShortLink_TwoPosts_ReturnSameCode()
    {
        Guid materialId = await SeedMaterialAsync(Guid.NewGuid());
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        ShortLinkDto firstDto = await ReadResultAsync<ShortLinkDto>(first);

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        ShortLinkDto secondDto = await ReadResultAsync<ShortLinkDto>(second);

        Assert.Equal(firstDto.Code, secondDto.Code);
        Assert.Equal(ShortLink.CODE_LENGTH, firstDto.Code.Length);
        Assert.Matches("^[0-9A-Za-z]+$", firstDto.Code);

        await ExecuteInDb(async db =>
            Assert.Equal(1, await db.ShortLinks.CountAsync(l => l.MaterialId == materialId)));
    }

    [Fact]
    public async Task GetOrCreateShortLink_UnknownMaterial_Returns404()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/short-links/materials/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrCreateShortLink_Anonymous_Returns401()
    {
        Guid materialId = await SeedMaterialAsync(Guid.NewGuid());
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResolveShortLink_KnownCode_AnonymousRedirects302ToMaterialPage()
    {
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        ShortLinkDto dto = await ReadResultAsync<ShortLinkDto>(createResponse);

        // _noRedirectClient без Authorization header — анонимный resolve.
        HttpResponseMessage resolveResponse = await _noRedirectClient.GetAsync(
            $"/short-links/{dto.Code}/resolve");

        Assert.Equal(HttpStatusCode.Redirect, resolveResponse.StatusCode);
        Assert.Equal($"/knowledge-base/{materialId}", resolveResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ResolveShortLink_UnknownCode_Returns404()
    {
        HttpResponseMessage response = await _noRedirectClient.GetAsync(
            "/short-links/unknown1/resolve");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMaterial_WithShortLink_CascadesAtDbLevel()
    {
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        ShortLinkDto dto = await ReadResultAsync<ShortLinkDto>(createResponse);

        // FK ON DELETE CASCADE: hard-delete материала не должен падать и обязан снести short_link.
        AuthenticateAsAdmin();
        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync($"/materials/{materialId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        await ExecuteInDb(async db =>
            Assert.False(await db.ShortLinks.AnyAsync(l => l.MaterialId == materialId)));

        HttpResponseMessage resolveResponse = await _noRedirectClient.GetAsync(
            $"/short-links/{dto.Code}/resolve");
        Assert.Equal(HttpStatusCode.NotFound, resolveResponse.StatusCode);
    }

    [Fact]
    public async Task GetOrCreateShortLink_DraftMaterial_Returns404()
    {
        // DRAFT не должен подтверждаться шортлинком — existence oracle для чужих черновиков.
        Guid materialId = await SeedMaterialAsync(Guid.NewGuid(), publish: false);
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/short-links/materials/{materialId}", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SeedMaterialAsync(Guid authorId, bool publish = true)
    {
        Guid materialId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create($"Материал {Guid.NewGuid():N}").Value,
                MaterialKind.ARTICLE,
                AccessType.ENROLLED);
            material.SetContent(MarkdownContent.Create("# Тело материала").Value);
            if (publish)
            {
                UnitResult<Error> publishResult = material.Publish();
                Assert.True(publishResult.IsSuccess);
            }

            db.Materials.Add(material);
            await db.SaveChangesAsync();
            materialId = material.Id;
        });
        return materialId;
    }
}
