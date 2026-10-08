using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ListPlanGrantsTests : AccessServiceTestsBase
{
    public ListPlanGrantsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_lists_grants_on_own_plan()
    {
        Guid planId = await CreatePlanAsync();
        await IssueGrantAsync(planId, Guid.NewGuid());
        await IssueGrantAsync(planId, Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/access/plans/{planId}/grants/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PlanGrantsPageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal(2, envelope!.Result!.Items.Count);
        Assert.Null(envelope.Result.NextCursor);
    }

    [Fact]
    public async Task Cursor_pagination_returns_next_page_then_terminates()
    {
        Guid planId = await CreatePlanAsync();
        for (int i = 0; i < 3; i++)
        {
            await IssueGrantAsync(planId, Guid.NewGuid());
        }

        HttpResponseMessage firstPage = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?limit=2");
        Envelope<PlanGrantsPageDto>? page1 =
            await firstPage.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page1?.Result);
        Assert.Equal(2, page1!.Result!.Items.Count);
        Assert.NotNull(page1.Result.NextCursor);

        HttpResponseMessage secondPage = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?limit=2&cursor={Uri.EscapeDataString(page1.Result.NextCursor!)}");
        Envelope<PlanGrantsPageDto>? page2 =
            await secondPage.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page2?.Result);
        Assert.Single(page2!.Result!.Items);
        Assert.Null(page2.Result.NextCursor);

        // Sanity: cross-page IDs не пересекаются.
        HashSet<Guid> ids = [
            ..page1.Result.Items.Select(i => i.Id),
            ..page2.Result.Items.Select(i => i.Id),
        ];
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Search_filters_by_lookup_user_ids()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid otherRecipient = Guid.NewGuid();
        await IssueGrantAsync(planId, recipient);
        await IssueGrantAsync(planId, otherRecipient);

        // Pre-populate AuthService user dictionary — fake's SearchUsers фильтрует
        // по полям этой коллекции. Совпадение по name "Alice" → handler получает
        // [recipient] → grants отфильтруются по этому userId.
        Factory.AuthClient.UsersById[recipient] =
            new AuthService.Contracts.AuthUserLookupDto(recipient, "Alice", "alice", "alice@x.io", null);
        Factory.AuthClient.UsersById[otherRecipient] =
            new AuthService.Contracts.AuthUserLookupDto(otherRecipient, "Bob", "bob", "bob@x.io", null);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?search=alice");
        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page?.Result);
        Assert.Single(page!.Result!.Items);
        Assert.Equal(recipient, page.Result.Items[0].UserId);
    }

    [Fact]
    public async Task Search_does_not_filter_by_user_email()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        await IssueGrantAsync(planId, recipient);

        Factory.AuthClient.UsersById[recipient] =
            new AuthService.Contracts.AuthUserLookupDto(
                recipient,
                "Email Only",
                "email_only_user",
                "grant-email-needle@x.io",
                null);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?search=grant-email-needle");
        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();

        Assert.NotNull(page?.Result);
        Assert.Empty(page!.Result!.Items);
    }

    [Fact]
    public async Task Search_filters_by_telegram_username()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid otherRecipient = Guid.NewGuid();
        await IssueGrantAsync(planId, recipient);
        await IssueGrantAsync(planId, otherRecipient);

        Factory.AuthClient.UsersById[recipient] =
            new AuthService.Contracts.AuthUserLookupDto(
                recipient,
                "Telegram Student",
                "telegram_student",
                "telegram-student@x.io",
                null,
                "grant_tg_unique");
        Factory.AuthClient.UsersById[otherRecipient] =
            new AuthService.Contracts.AuthUserLookupDto(
                otherRecipient,
                "Other",
                "other",
                "other@x.io",
                null);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?search=grant_tg_unique");
        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();

        Assert.NotNull(page?.Result);
        Assert.Single(page!.Result!.Items);
        Assert.Equal(recipient, page.Result.Items[0].UserId);
    }

    [Fact]
    public async Task Malformed_cursor_degrades_to_first_page()
    {
        // DecodeCursor catches FormatException и возвращает (null, null). Endpoint
        // должен отдать первую страницу, не упасть с 500/400.
        Guid planId = await CreatePlanAsync();
        await IssueGrantAsync(planId, Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?cursor=not-a-base64-string");
        response.EnsureSuccessStatusCode();

        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page?.Result);
        Assert.Single(page!.Result!.Items);
    }

    [Fact]
    public async Task Limit_above_max_is_clamped_to_100()
    {
        // limit=999 должен clamp'нуться до MAX_LIMIT=100 без ошибки.
        Guid planId = await CreatePlanAsync();
        await IssueGrantAsync(planId, Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?limit=999");
        response.EnsureSuccessStatusCode();

        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page?.Result);
        Assert.Single(page!.Result!.Items);
    }

    [Fact]
    public async Task Search_with_no_matches_returns_empty()
    {
        Guid planId = await CreatePlanAsync();
        await IssueGrantAsync(planId, Guid.NewGuid());

        // SearchResults для "zzz" пустой (no AuthService matches) → page пустая.
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/grants/?search=zzz");
        Envelope<PlanGrantsPageDto>? page =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        Assert.NotNull(page?.Result);
        Assert.Empty(page!.Result!.Items);
        Assert.Null(page.Result.NextCursor);
    }

    [Fact]
    public async Task Foreign_author_cannot_list()
    {
        Guid planId = await CreatePlanAsync();

        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/access/plans/{planId}/grants/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Plan_not_found_returns_404()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{Guid.NewGuid()}/grants/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoked_grant_still_appears_in_list()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();

        AdminGrantRequest grantRequest = new(recipient, planId, null);
        HttpResponseMessage grantResp = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", grantRequest);
        grantResp.EnsureSuccessStatusCode();
        Guid grantId = (await grantResp.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;

        HttpResponseMessage revoke = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke",
            JsonContent.Create(new RevokeGrantRequest("test")));
        revoke.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/access/plans/{planId}/grants/");
        response.EnsureSuccessStatusCode();

        Envelope<PlanGrantsPageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantsPageDto>>();
        PlanGrantDto returned = envelope!.Result!.Items.Single();
        Assert.Equal(grantId, returned.Id);
        Assert.Equal(nameof(PlanGrantStatus.REVOKED), returned.Status);
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "list-grants-test",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }

    private async Task IssueGrantAsync(Guid planId, Guid recipient)
    {
        AdminGrantRequest request = new(recipient, planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        response.EnsureSuccessStatusCode();
    }
}
