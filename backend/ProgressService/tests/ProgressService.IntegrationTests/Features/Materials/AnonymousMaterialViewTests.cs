using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Materials;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Materials;

/// <summary>
///     Issue #234 — публичный счётчик уникальных просмотров материала.
///     Эндпоинты <c>POST /progress/materials/{id}/anonymous-view</c> и
///     <c>POST /progress/materials/views/counts</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class AnonymousMaterialViewTests : ProgressServiceTestsBase
{
    public AnonymousMaterialViewTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task RecordAnonymousView_FirstCall_CreatesRow()
    {
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();
        string anonymousId = Guid.NewGuid().ToString();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(anonymousId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int count = await ExecuteInDb(db =>
            db.AnonymousMaterialViews.CountAsync(v =>
                v.AnonymousId == anonymousId && v.MaterialId == materialId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordAnonymousView_Twice_IsIdempotent()
    {
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();
        string anonymousId = Guid.NewGuid().ToString();

        await PostAsJsonAsync(
            $"/progress/materials/{materialId}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(anonymousId));
        HttpResponseMessage second = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(anonymousId));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        int count = await ExecuteInDb(db =>
            db.AnonymousMaterialViews.CountAsync(v =>
                v.AnonymousId == anonymousId && v.MaterialId == materialId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordAnonymousView_NormalizesUuidCasing()
    {
        // UUID в разном casing'е (upper/lower/braces) должен сводиться к одной
        // канонической форме, иначе один cookie мог бы продолбать дедуп.
        RemoveAuthentication();
        Guid materialId = Guid.NewGuid();
        Guid uuid = Guid.NewGuid();

        await PostAsJsonAsync(
            $"/progress/materials/{materialId}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(uuid.ToString("D").ToUpperInvariant()));
        await PostAsJsonAsync(
            $"/progress/materials/{materialId}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(uuid.ToString("D").ToLowerInvariant()));

        int count = await ExecuteInDb(db =>
            db.AnonymousMaterialViews.CountAsync(v => v.MaterialId == materialId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RecordAnonymousView_InvalidUuid_Returns400()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{Guid.NewGuid()}/anonymous-view",
            new RecordAnonymousMaterialViewRequest("not-a-uuid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RecordAnonymousView_EmptyAnonymousId_Returns400()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{Guid.NewGuid()}/anonymous-view",
            new RecordAnonymousMaterialViewRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialViewsCounts_SumsAuthAndAnonymous_Unique()
    {
        Guid materialA = Guid.NewGuid();
        Guid materialB = Guid.NewGuid();
        Guid materialC = Guid.NewGuid();

        await SeedAuthViewsAsync(materialA, count: 3);
        await SeedAnonymousViewsAsync(materialA, count: 2);
        await SeedAuthViewsAsync(materialB, count: 5);
        // materialC — без просмотров: должен отсутствовать в ответе.

        RemoveAuthentication();

        HttpResponseMessage response = await PostAsJsonAsync(
            "/progress/materials/views/counts",
            new GetMaterialViewsCountsRequest(new[] { materialA, materialB, materialC }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetMaterialViewsCountsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetMaterialViewsCountsResponse>>();
        Assert.NotNull(envelope?.Result);
        GetMaterialViewsCountsResponse body = envelope.Result!;
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(5L, body.Items.Single(i => i.MaterialId == materialA).Count);
        Assert.Equal(5L, body.Items.Single(i => i.MaterialId == materialB).Count);
        Assert.DoesNotContain(body.Items, i => i.MaterialId == materialC);
    }

    [Fact]
    public async Task GetMaterialViewsCounts_ExceedsMaxBatchSize_Returns400()
    {
        RemoveAuthentication();

        // 257 > MAX_BATCH_SIZE (256) — должен отказать на этапе валидатора.
        Guid[] tooMany = Enumerable.Range(0, 257).Select(_ => Guid.NewGuid()).ToArray();

        HttpResponseMessage response = await PostAsJsonAsync(
            "/progress/materials/views/counts",
            new GetMaterialViewsCountsRequest(tooMany));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialViewsCounts_EmptyMaterialIds_Returns400()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await PostAsJsonAsync(
            "/progress/materials/views/counts",
            new GetMaterialViewsCountsRequest(Array.Empty<Guid>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaterialHardDeleted_CascadesAnonymousViews()
    {
        Guid materialId = Guid.NewGuid();
        await SeedAnonymousViewsAsync(materialId, count: 3);

        // Прямой вызов handler-а: integration event на уровне Wolverine отрабатывается
        // в существующих тестах MaterialHardDeleted (см. MaterialHardDeletedHandlerTests).
        // Здесь проверяем только новую часть каскада — anonymous_material_views.
        await InvokeMessageAndWaitAsync(
            new Shared.Messaging.IntegrationEvents.Education.Events.MaterialHardDeleted(materialId));

        int remaining = await ExecuteInDb(db =>
            db.AnonymousMaterialViews.CountAsync(v => v.MaterialId == materialId));
        Assert.Equal(0, remaining);
    }

    private async Task SeedAuthViewsAsync(Guid materialId, int count)
    {
        await ExecuteInDb(async db =>
        {
            for (int i = 0; i < count; i++)
            {
                MaterialView view = MaterialView.Create(Guid.NewGuid(), materialId).Value;
                db.MaterialViews.Add(view);
            }
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedAnonymousViewsAsync(Guid materialId, int count)
    {
        await ExecuteInDb(async db =>
        {
            for (int i = 0; i < count; i++)
            {
                AnonymousMaterialView view =
                    AnonymousMaterialView.Create(Guid.NewGuid().ToString(), materialId).Value;
                db.AnonymousMaterialViews.Add(view);
            }
            await db.SaveChangesAsync();
        });
    }
}
