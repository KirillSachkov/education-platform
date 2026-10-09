using CSharpFunctionalExtensions;
using EducationContentService.Core.Features.Materials.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class SitemapExportTests : EducationContentServiceTestsBase
{
    public SitemapExportTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetSitemapExport_Anonymous_ReturnsOnlyPublishedEntities()
    {
        // Arrange: PUBLISHED-сущности обоих типов + DRAFT-двойники,
        // ENROLLED-материал (access_type не влияет на попадание в sitemap).
        CancellationToken ct = CancellationToken.None;
        Guid publicMaterialId = await SeedMaterialAsync(AccessType.PUBLIC, publish: true, ct);
        Guid enrolledMaterialId = await SeedMaterialAsync(AccessType.ENROLLED, publish: true, ct);
        Guid draftMaterialId = await SeedMaterialAsync(AccessType.PUBLIC, publish: false, ct);

        Guid publishedCollectionId = await SeedCollectionAsync(publish: true, ct);
        Guid draftCollectionId = await SeedCollectionAsync(publish: false, ct);

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/materials/sitemap-export", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        SitemapExportDto dto = await ReadResultAsync<SitemapExportDto>(response);

        Assert.Contains(dto.Materials, m => m.Id == publicMaterialId);
        Assert.Contains(dto.Materials, m => m.Id == enrolledMaterialId);
        Assert.DoesNotContain(dto.Materials, m => m.Id == draftMaterialId);

        Assert.Contains(dto.Collections, c => c.Id == publishedCollectionId);
        Assert.DoesNotContain(dto.Collections, c => c.Id == draftCollectionId);


        Assert.All(dto.Materials, m => Assert.NotEqual(default, m.UpdatedAt));
    }

    [Fact]
    public async Task GetSitemapExport_ResponsePayload_ContainsNoTitleOrContent()
    {
        // Метаданные-only контракт: наружу идут только id/slug + updated_at —
        // ни title-ключей, ни тела материала в JSON быть не должно.
        CancellationToken ct = CancellationToken.None;
        const string SECRET_TITLE = "SecretSitemapTitle";
        await SeedMaterialAsync(AccessType.PUBLIC, publish: true, ct, title: SECRET_TITLE);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/materials/sitemap-export", ct);

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(ct);

        Assert.DoesNotContain("\"title\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"content\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SECRET_TITLE, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"roadmaps\"", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SeedMaterialAsync(
        AccessType accessType,
        bool publish,
        CancellationToken ct,
        string? title = null)
    {
        Guid materialId = Guid.Empty;
        string uniqueTitle = title ?? $"Mat-{Guid.NewGuid():N}"[..20];

        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: Guid.NewGuid(),
                title: Title.Create(uniqueTitle).Value,
                accessType: accessType);
            material.SetContent(MarkdownContent.Create("body").Value);

            if (publish)
            {
                UnitResult<Error> result = material.Publish();
                Assert.True(result.IsSuccess);
            }

            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });

        return materialId;
    }

    private async Task<Guid> SeedCollectionAsync(bool publish, CancellationToken ct)
    {
        Guid collectionId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var collection = new Collection(
                authorId: Guid.NewGuid(),
                title: Title.Create($"Col-{Guid.NewGuid():N}"[..20]).Value);

            if (publish)
            {
                UnitResult<Error> result = collection.Publish(hasAnyItem: true);
                Assert.True(result.IsSuccess);
            }

            db.Set<Collection>().Add(collection);
            await db.SaveChangesAsync(ct);
            collectionId = collection.Id;
        });

        return collectionId;
    }


}