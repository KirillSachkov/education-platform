using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Collections;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class BulkSetCollectionItemsAccessTypeTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public BulkSetCollectionItemsAccessTypeTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BulkSet_ChangesAccessType_ForAllMaterials_PublishesEventPerChanged()
    {
        // Подборка с 2 секциями + 3 материала (1 в первой, 2 во второй). После bulk → ENROLLED:
        // все 3 материала меняют access (initial PUBLIC), 3 события MaterialAccessChanged.
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionWithItems(
            initialAccessType: AccessType.PUBLIC,
            sectionsWithMaterialCounts: [1, 2],
            ct);

        _factory.OutboxCollector.Clear();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("ENROLLED"),
            ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<BulkSetItemsAccessTypeResponse>(resp);
        Assert.NotNull(body);
        Assert.Equal(3, body.UpdatedCount);
        Assert.Equal(0, body.SkippedCount);
        Assert.Equal(0, body.SkippedNotOwnedCount);
        Assert.Equal(3, body.TotalCount);

        await ExecuteInDb(async db =>
        {
            var materials = await db.Set<Material>().ToListAsync(ct);
            Assert.All(materials, m => Assert.Equal(AccessType.ENROLLED, m.AccessType));
        });

        IReadOnlyList<MaterialAccessChanged> published =
            _factory.OutboxCollector.OfType<MaterialAccessChanged>().ToList();
        Assert.Equal(3, published.Count);
        Assert.All(published, e => Assert.Equal("ENROLLED", e.AccessType));
    }

    [Fact]
    public async Task BulkSet_SameAccessType_SkipsAllAndPublishesNoEvents()
    {
        // Все материалы уже PUBLIC → запрос PUBLIC → updated=0, skipped=N, события не публикуются.
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionWithItems(
            initialAccessType: AccessType.PUBLIC,
            sectionsWithMaterialCounts: [3],
            ct);

        _factory.OutboxCollector.Clear();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("PUBLIC"),
            ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<BulkSetItemsAccessTypeResponse>(resp);
        Assert.NotNull(body);
        Assert.Equal(0, body.UpdatedCount);
        Assert.Equal(3, body.SkippedCount);
        Assert.Equal(0, body.SkippedNotOwnedCount);
        Assert.Equal(3, body.TotalCount);

        Assert.Empty(_factory.OutboxCollector.OfType<MaterialAccessChanged>());
    }

    [Fact]
    public async Task BulkSet_EmptyCollection_ReturnsZeroes_NoEvents()
    {
        // Подборка без секций — happy-path no-op.
        CancellationToken ct = CancellationToken.None;
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Empty", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        _factory.OutboxCollector.Clear();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("REGISTERED"),
            ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<BulkSetItemsAccessTypeResponse>(resp);
        Assert.NotNull(body);
        Assert.Equal(0, body.UpdatedCount);
        Assert.Equal(0, body.SkippedCount);
        Assert.Equal(0, body.SkippedNotOwnedCount);
        Assert.Equal(0, body.TotalCount);
        Assert.Empty(_factory.OutboxCollector.OfType<MaterialAccessChanged>());
    }

    [Fact]
    public async Task BulkSet_CrossAuthorMaterial_NonAdmin_SkipsAsNotOwned()
    {
        // Не-админ автор A создаёт подборку, в ней лежит материал автора B (cross-author).
        // Bulk → материал B пропускается без error'а, увеличивает SkippedNotOwnedCount.
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        AuthenticateAs(authorA, "platform-author");

        // Подборка автора A.
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Mixed", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        // Section и материалы: 1 принадлежит автору A, 1 — автору B.
        HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        secResp.EnsureSuccessStatusCode();
        Guid sectionId = await ReadResultAsync<Guid>(secResp);

        Guid materialA = await CreateMaterialInDbWithAuthor(authorA, AccessType.PUBLIC, ct);
        Guid materialB = await CreateMaterialInDbWithAuthor(Guid.NewGuid(), AccessType.PUBLIC, ct);

        await AddItemToSection(collectionId, sectionId, materialA, ct);
        await AddItemToSection(collectionId, sectionId, materialB, ct);

        _factory.OutboxCollector.Clear();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("REGISTERED"),
            ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<BulkSetItemsAccessTypeResponse>(resp);
        Assert.NotNull(body);
        Assert.Equal(1, body.UpdatedCount);
        Assert.Equal(0, body.SkippedCount);
        Assert.Equal(1, body.SkippedNotOwnedCount);
        Assert.Equal(2, body.TotalCount);

        await ExecuteInDb(async db =>
        {
            Material mA = await db.Set<Material>().FirstAsync(m => m.Id == materialA, ct);
            Material mB = await db.Set<Material>().FirstAsync(m => m.Id == materialB, ct);
            Assert.Equal(AccessType.REGISTERED, mA.AccessType);
            Assert.Equal(AccessType.PUBLIC, mB.AccessType); // не трогали
        });

        // Событие — ровно одно (на materialA).
        MaterialAccessChanged ev = Assert.Single(_factory.OutboxCollector.OfType<MaterialAccessChanged>());
        Assert.Equal(materialA, ev.MaterialId);
        Assert.Equal("REGISTERED", ev.AccessType);
        Assert.Equal(authorA, ev.AuthorId);
    }

    [Fact]
    public async Task BulkSet_NotCollectionAuthor_ReturnsError()
    {
        // Юзер B пытается изменить подборку юзера A → fail authorship.
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Theirs", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        // Переключаемся на чужого юзера.
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("ENROLLED"),
            ct);

        // Authorship error → 4xx (точный код зависит от mapping'а в EducationErrors).
        Assert.True(
            resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
            $"Expected 4xx for unauthorized author, got {resp.StatusCode}.");
    }

    [Fact]
    public async Task BulkSet_InvalidAccessType_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionWithItems(
            initialAccessType: AccessType.PUBLIC,
            sectionsWithMaterialCounts: [1],
            ct);

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("WIBBLE"),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BulkSet_CollectionNotFound_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid unknownId = Guid.NewGuid();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{unknownId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("REGISTERED"),
            ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task BulkSet_DuplicateMaterialInTwoSections_PublishesOneEvent()
    {
        // Идемпотентность: один материал в двух секциях подборки → счётчик total=1, event=1.
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Dup", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        Guid sec1 = await AddSection(collectionId, ct);
        Guid sec2 = await AddSection(collectionId, ct);

        Guid materialId = await CreateMaterialInDbWithAuthor(authorA, AccessType.PUBLIC, ct);

        await AddItemToSection(collectionId, sec1, materialId, ct);
        await AddItemToSection(collectionId, sec2, materialId, ct);

        _factory.OutboxCollector.Clear();

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/collections/{collectionId}/items/access-type",
            new BulkSetItemsAccessTypeRequest("REGISTERED"),
            ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadResultAsync<BulkSetItemsAccessTypeResponse>(resp);
        Assert.NotNull(body);
        Assert.Equal(1, body.UpdatedCount);
        Assert.Equal(1, body.TotalCount);
        Assert.Single(_factory.OutboxCollector.OfType<MaterialAccessChanged>());
    }

    // ----- helpers -----

    private async Task<Guid> CreateCollectionWithItems(
        AccessType initialAccessType,
        int[] sectionsWithMaterialCounts,
        CancellationToken ct)
    {
        // Default-юзер из InitializeAsync — admin; используем admin для happy-path тестов,
        // материалы создаются с admin'ским userId (поскольку IsAdmin bypass'ит cross-author
        // фильтр — никаких SkippedNotOwned).
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Test", null, null),
            ct);
        createResp.EnsureSuccessStatusCode();
        Guid collectionId = await ReadResultAsync<Guid>(createResp);

        foreach (int count in sectionsWithMaterialCounts)
        {
            Guid sectionId = await AddSection(collectionId, ct);
            for (int i = 0; i < count; i++)
            {
                Guid materialId = await CreateMaterialInDbWithAuthor(Guid.NewGuid(), initialAccessType, ct);
                await AddItemToSection(collectionId, sectionId, materialId, ct);
            }
        }

        return collectionId;
    }

    private async Task<Guid> AddSection(Guid collectionId, CancellationToken ct)
    {
        HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        secResp.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(secResp);
    }

    private async Task AddItemToSection(Guid collectionId, Guid sectionId, Guid materialId, CancellationToken ct)
    {
        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(materialId),
            ct);
        itemResp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateMaterialInDbWithAuthor(Guid authorId, AccessType accessType, CancellationToken ct)
    {
        Guid materialId = Guid.NewGuid();
        string uniqueTitle = $"Mat-{Guid.NewGuid():N}".Substring(0, 32);
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: authorId,
                title: Title.Create(uniqueTitle).Value,
                accessType: accessType);
            db.Set<Material>().Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }
}
