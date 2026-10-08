using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ProgressService.Contracts.HttpCommunication;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMaterialsCardMetaTests : EducationContentServiceTestsBase
{
    public GetMaterialsCardMetaTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CardMeta_Anonymous_ReturnsViewsAndDuration()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid videoAssetId = Guid.CreateVersion7();

        Guid materialId = await CreateVideoMaterialAsync("Видео-урок", authorId, videoAssetId, ct);

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.GetVideosBatchAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(videoAssetId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<GetPublicVideoResponse>?, Error>(
                [new GetPublicVideoResponse(videoAssetId, null, 125.0)]));

        var progressClient = Services.GetRequiredService<IProgressServiceClient>();
        progressClient.GetMaterialViewsCountsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(materialId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, long>, Error>(
                new Dictionary<Guid, long> { [materialId] = 7L }));

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/materials/card-meta",
            new GetMaterialsCardMetaRequest([materialId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCardMetaDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCardMetaDto>>>();
        Assert.NotNull(envelope?.Result);

        MaterialCardMetaDto meta = Assert.Single(envelope.Result);
        Assert.Equal(materialId, meta.MaterialId);
        Assert.Equal(7L, meta.ViewsCount);
        Assert.Equal(125.0, meta.DurationSeconds);
    }

    [Fact]
    public async Task CardMeta_DraftMaterial_Excluded()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid publishedId = await CreateArticleMaterialAsync("Опубликован", authorId, publish: true, ct);
        Guid draftId = await CreateArticleMaterialAsync("Черновик", authorId, publish: false, ct);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/materials/card-meta",
            new GetMaterialsCardMetaRequest([publishedId, draftId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCardMetaDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCardMetaDto>>>();
        Assert.NotNull(envelope?.Result);

        MaterialCardMetaDto meta = Assert.Single(envelope.Result);
        Assert.Equal(publishedId, meta.MaterialId);
        // ARTICLE без видео — длительности нет, просмотров нет (default-mock отдаёт пустую map).
        Assert.Null(meta.DurationSeconds);
        Assert.Equal(0L, meta.ViewsCount);
    }

    [Fact]
    public async Task CardMeta_EnrichesAuthorDisplayName()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = await CreateArticleMaterialAsync("Статья соавтора", authorId, publish: true, ct);

        var authorClient = Services.GetRequiredService<IAuthorLookupClient>();
        authorClient.GetAuthorsByIdsAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(authorId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(
                new Dictionary<Guid, AuthorCreditDto>
                {
                    [authorId] = new AuthorCreditDto(authorId, "Пётр Соавтор", AvatarId: null),
                }));

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/materials/card-meta",
            new GetMaterialsCardMetaRequest([materialId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<MaterialCardMetaDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<MaterialCardMetaDto>>>();
        Assert.NotNull(envelope?.Result);

        MaterialCardMetaDto meta = Assert.Single(envelope.Result);
        Assert.Equal(materialId, meta.MaterialId);
        Assert.Equal("Пётр Соавтор", meta.AuthorDisplayName);
    }

    [Fact]
    public async Task CardMeta_OverBatchLimit_Returns400()
    {
        RemoveAuthentication();

        Guid[] tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/materials/card-meta",
            new GetMaterialsCardMetaRequest(tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreateVideoMaterialAsync(
        string title, Guid authorId, Guid videoAssetId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.VIDEO, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nКонспект").Value,
                MaterialKind.VIDEO,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            material.AttachVideo(VideoId.Create(videoAssetId).Value);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateArticleMaterialAsync(
        string title, Guid authorId, bool publish, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            if (publish)
            {
                Assert.True(material.Publish().IsSuccess);
            }
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }
}
