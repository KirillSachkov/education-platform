using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     Tests for issue #216: создание материала «из подборки» через picker. Backend атомарно
///     добавляет новый материал в collection_items в той же транзакции, что и создание
///     самого материала. Проверяем happy-path + ownership + валидация (CollectionId без
///     SectionId и наоборот).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class CreateDraftMaterialCollectionTests : EducationContentServiceTestsBase
{
    public CreateDraftMaterialCollectionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateDraftMaterial_WithCollectionAndSection_AttachesToCollection()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        (Guid collectionId, Guid sectionId) = await SeedCollectionWithSectionAsync(authorId, ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/materials/draft",
            new CreateDraftMaterialRequest(CollectionId: collectionId, SectionId: sectionId),
            ct);

        resp.EnsureSuccessStatusCode();
        Guid materialId = await ReadResultAsync<Guid>(resp);

        await ExecuteInDb(async db =>
        {
            CollectionItem? item = await db.Set<CollectionItem>()
                .FirstOrDefaultAsync(
                    i => i.SectionId == sectionId
                         && i.ItemType == CollectionItemType.MATERIAL
                         && i.ReferenceId == materialId,
                    ct);
            Assert.NotNull(item);
        });
    }

    [Fact]
    public async Task CreateDraftMaterial_WithForeignCollection_ReturnsForbidden()
    {
        // IDOR-защита: автор не может прикрепить свой материал в чужую подборку.
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.CreateVersion7();
        Guid attackerId = Guid.CreateVersion7();

        (Guid collectionId, Guid sectionId) = await SeedCollectionWithSectionAsync(ownerId, ct);

        AuthenticateAs(attackerId, "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/materials/draft",
            new CreateDraftMaterialRequest(CollectionId: collectionId, SectionId: sectionId),
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // Side-effect-free: материал не создан и в коллекцию не добавлен.
        await ExecuteInDb(async db =>
        {
            int itemCount = await db.Set<CollectionItem>()
                .CountAsync(i => i.SectionId == sectionId, ct);
            Assert.Equal(0, itemCount);
            int materialsByAttacker = await db.Materials.CountAsync(m => m.AuthorId == attackerId, ct);
            Assert.Equal(0, materialsByAttacker);
        });
    }

    [Fact]
    public async Task CreateDraftMaterial_WithSectionIdWithoutCollectionId_ReturnsValidationError()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/materials/draft",
            // Половинчатый payload — намеренно создаём оба не парой.
            new CreateDraftMaterialRequest(CollectionId: null, SectionId: Guid.NewGuid()),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDraftMaterial_WithCollectionIdWithoutSectionId_ReturnsValidationError()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/materials/draft",
            new CreateDraftMaterialRequest(CollectionId: Guid.NewGuid(), SectionId: null),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDraftMaterial_WithSectionFromDifferentCollection_ReturnsNotFound()
    {
        // Кросс-привязка: SectionId принадлежит другой подборке, но передан с CollectionId.
        // Handler должен 404'ить на section, не дублируя её под чужую подборку.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        (Guid collectionAId, _) = await SeedCollectionWithSectionAsync(authorId, ct);
        (_, Guid sectionBId) = await SeedCollectionWithSectionAsync(authorId, ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/materials/draft",
            new CreateDraftMaterialRequest(CollectionId: collectionAId, SectionId: sectionBId),
            ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private async Task<(Guid CollectionId, Guid SectionId)> SeedCollectionWithSectionAsync(
        Guid authorId, CancellationToken ct)
    {
        Guid collectionId = Guid.Empty;
        Guid sectionId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var collection = new Collection(
                authorId,
                Title.Create("Test").Value,
                courseId: null);
            db.Set<Collection>().Add(collection);

            var section = new CollectionSection(
                collection.Id,
                title: null,
                description: null,
                SortKey.Initial());
            db.Set<CollectionSection>().Add(section);

            await db.SaveChangesAsync(ct);
            collectionId = collection.Id;
            sectionId = section.Id;
        });
        return (collectionId, sectionId);
    }
}
