using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.HomePins;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.HomePins;

/// <summary>
///     Author/admin-side home-pins CRUD: <c>GET/POST/PATCH/POST .../order/DELETE
///     /access/plans/{planId}/home-pins/...</c>. Permission <c>plans.manage</c> + ownership.
///     Epic #397.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class HomePinsCrudTests : AccessServiceTestsBase
{
    public HomePinsCrudTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Add_pin_persists_and_is_listed_with_enriched_title()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId, title: "Урок про DI");

        Guid pinId = await AddPinAsync(planId, materialId, note: "почитать первым");

        IReadOnlyList<HomePinListItemDto> pins = await ListPinsAsync(planId);
        HomePinListItemDto pin = Assert.Single(pins);
        Assert.Equal(pinId, pin.PinId);
        Assert.Equal(materialId, pin.MaterialId);
        Assert.Equal("Урок про DI", pin.Title);
        Assert.Equal("почитать первым", pin.Note);
    }

    [Fact]
    public async Task Add_duplicate_material_returns_conflict()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);

        await AddPinAsync(planId, materialId);

        HttpResponseMessage dup = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, Note: null));

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Envelope? env = await dup.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(env!.Error!.Messages, m => string.Equals(m.Code, "home_pin.already.pinned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_pin_when_material_absent_in_ecs_returns_not_found()
    {
        Guid planId = await CreatePlanAsync();
        // Material NOT registered in the fake → ECS summaries returns it absent.
        Guid materialId = Guid.NewGuid();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, Note: null));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Envelope? env = await resp.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(env!.Error!.Messages, m => string.Equals(m.Code, "home_pin.material.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_note_persists()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);
        Guid pinId = await AddPinAsync(planId, materialId, note: "old");

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}/home-pins/{pinId}/",
            new UpdateHomePinNoteRequest("new note"));
        resp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> pins = await ListPinsAsync(planId);
        Assert.Equal("new note", Assert.Single(pins).Note);
    }

    [Fact]
    public async Task Update_note_to_null_clears_it()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);
        Guid pinId = await AddPinAsync(planId, materialId, note: "old");

        HttpResponseMessage resp = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}/home-pins/{pinId}/",
            new UpdateHomePinNoteRequest(null));
        resp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> pins = await ListPinsAsync(planId);
        Assert.Null(Assert.Single(pins).Note);
    }

    [Fact]
    public async Task Reorder_moves_pin_before_another()
    {
        Guid planId = await CreatePlanAsync();
        Guid mat1 = Guid.NewGuid();
        Guid mat2 = Guid.NewGuid();
        Guid mat3 = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(mat1);
        Factory.EduClient.AddMaterialSummary(mat2);
        Factory.EduClient.AddMaterialSummary(mat3);

        Guid pin1 = await AddPinAsync(planId, mat1);
        Guid pin2 = await AddPinAsync(planId, mat2);
        Guid pin3 = await AddPinAsync(planId, mat3);

        // Initial order: pin1, pin2, pin3.
        IReadOnlyList<HomePinListItemDto> before = await ListPinsAsync(planId);
        Assert.Equal(new[] { pin1, pin2, pin3 }, before.Select(p => p.PinId));

        // Move pin3 to the very front. New slot has pin1 as its upper neighbour and
        // nothing below → BeforeId=null, AfterId=pin1 (BeforeId/AfterId name the new
        // lower/upper neighbours of the moved pin).
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/{pin3}/order/",
            new ReorderHomePinRequest(BeforeId: null, AfterId: pin1));
        resp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> after = await ListPinsAsync(planId);
        Assert.Equal(new[] { pin3, pin1, pin2 }, after.Select(p => p.PinId));
        // Sort keys are strictly ascending in returned order.
        Assert.True(StringComparer.Ordinal.Compare(after[0].SortKey, after[1].SortKey) < 0);
        Assert.True(StringComparer.Ordinal.Compare(after[1].SortKey, after[2].SortKey) < 0);
    }

    [Fact]
    public async Task Reorder_between_two_pins_places_in_middle()
    {
        Guid planId = await CreatePlanAsync();
        Guid mat1 = Guid.NewGuid();
        Guid mat2 = Guid.NewGuid();
        Guid mat3 = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(mat1);
        Factory.EduClient.AddMaterialSummary(mat2);
        Factory.EduClient.AddMaterialSummary(mat3);

        Guid pin1 = await AddPinAsync(planId, mat1);
        Guid pin2 = await AddPinAsync(planId, mat2);
        Guid pin3 = await AddPinAsync(planId, mat3);

        // Move pin1 to between pin2 and pin3 — lower neighbour pin2, upper neighbour pin3.
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/{pin1}/order/",
            new ReorderHomePinRequest(BeforeId: pin2, AfterId: pin3));
        resp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> after = await ListPinsAsync(planId);
        Assert.Equal(new[] { pin2, pin1, pin3 }, after.Select(p => p.PinId));
    }

    [Fact]
    public async Task Delete_pin_removes_it()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);
        Guid pinId = await AddPinAsync(planId, materialId);

        HttpResponseMessage resp = await AppHttpClient.DeleteAsync(
            $"/access/plans/{planId}/home-pins/{pinId}/");
        resp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> pins = await ListPinsAsync(planId);
        Assert.Empty(pins);
    }

    [Fact]
    public async Task Delete_unknown_pin_returns_not_found()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage resp = await AppHttpClient.DeleteAsync(
            $"/access/plans/{planId}/home-pins/{Guid.NewGuid()}/");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Non_owner_author_gets_forbidden()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);

        // Different author — same role, different userId → ownership check fails.
        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage addResp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, Note: null));
        Assert.Equal(HttpStatusCode.Forbidden, addResp.StatusCode);

        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/home-pins/");
        Assert.Equal(HttpStatusCode.Forbidden, listResp.StatusCode);
    }

    [Fact]
    public async Task Admin_can_manage_any_plan()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);

        // Admin with a different userId — bypasses ownership.
        AuthenticateAsAdmin(Guid.NewGuid());

        HttpResponseMessage addResp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, Note: null));
        addResp.EnsureSuccessStatusCode();

        IReadOnlyList<HomePinListItemDto> pins = await ListPinsAsync(planId);
        Assert.Single(pins);
    }

    [Fact]
    public async Task Participant_lacking_plans_manage_gets_forbidden()
    {
        Guid planId = await CreatePlanAsync();
        Guid materialId = Guid.NewGuid();
        Factory.EduClient.AddMaterialSummary(materialId);

        // platform-participant has no plans.manage permission.
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage addResp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, Note: null));
        Assert.Equal(HttpStatusCode.Forbidden, addResp.StatusCode);

        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/home-pins/");
        Assert.Equal(HttpStatusCode.Forbidden, listResp.StatusCode);
    }

    // ───────────── Helpers ─────────────

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"plan-{Guid.NewGuid():N}",
            DisplayName: "Test Plan",
            ShortDescription: "desc",
            LongDescription: "long",
            CoverFileId: null,
            Features: new[] { "f1" },
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task<Guid> AddPinAsync(Guid planId, Guid materialId, string? note = null)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/home-pins/",
            new AddHomePinRequest(materialId, note));
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task<IReadOnlyList<HomePinListItemDto>> ListPinsAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/access/plans/{planId}/home-pins/");
        resp.EnsureSuccessStatusCode();
        Envelope<IReadOnlyList<HomePinListItemDto>>? env =
            await resp.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<HomePinListItemDto>>>();
        return env!.Result!;
    }
}
