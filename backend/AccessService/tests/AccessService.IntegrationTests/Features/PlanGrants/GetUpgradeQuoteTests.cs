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
public sealed class GetUpgradeQuoteTests : AccessServiceTestsBase
{
    public GetUpgradeQuoteTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{Guid.NewGuid()}/upgrade-quote/");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Plan_not_published_returns_NotFound()
    {
        Guid planId = await CreatePlanAsync(nameof(PlanTier.FULL_ALL), priceCents: 100_000);
        // Не публикуем — план остаётся draft (IsPublic=false).

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/upgrade-quote/");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task User_without_grants_gets_full_price_no_credit()
    {
        Guid planId = await CreatePlanAsync(nameof(PlanTier.FULL_ALL), priceCents: 100_000);
        await PublishAsync(planId);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(100_000, quote!.OriginalPriceCents);
        Assert.Equal(0, quote.CreditCents);
        Assert.Equal(100_000, quote.FinalPriceCents);
        Assert.False(quote.IsOwned);
        Assert.Empty(quote.Sources);
    }

    [Fact]
    public async Task Credit_is_summed_from_subset_grants_with_price_paid()
    {
        // Setup: автор создаёт COURSE-план (50 000 ₽) и FULL_ALL-план (100 000 ₽).
        // Юзер «купил» COURSE через PURCHASE-grant с pricePaidCents=50000.
        // Запрашивает quote на FULL_ALL → ожидаем credit=50000, final=50000.
        Guid authorId = DefaultUserId;
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 50_000_00, // 50 000 ₽ in cents
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);

        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00); // 100 000 ₽
        await PublishAsync(fullPlanId);

        // Симулируем PURCHASE: создаём grant руками с pricePaidCents=50_000_00.
        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.PURCHASE,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: 50_000_00);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(100_000_00, quote!.OriginalPriceCents);
        Assert.Equal(50_000_00, quote.CreditCents);
        Assert.Equal(50_000_00, quote.FinalPriceCents);
        Assert.False(quote.IsOwned);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(coursePlanId, source.PlanId);
        Assert.Equal(50_000_00, source.CreditCents);
        Assert.Equal("COURSE", source.PlanTier);
    }

    [Fact]
    public async Task Revoked_grant_contributes_no_credit()
    {
        // #414: REVOKED (включая refunded) grant НЕ даёт upgrade-credit. Setup как в
        // Credit_is_summed..., но grant отозван перед запросом quote → credit=0.
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 50_000_00,
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);

        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00);
        await PublishAsync(fullPlanId);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.PURCHASE,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: 50_000_00);
            // Отзываем (как при refund'е) — теперь grant не должен давать credit.
            grant.Revoke(Guid.NewGuid(), "refund: test");
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(100_000_00, quote!.OriginalPriceCents);
        Assert.Equal(0, quote.CreditCents);
        Assert.Equal(100_000_00, quote.FinalPriceCents);
        Assert.False(quote.IsOwned);
        Assert.Empty(quote.Sources);
    }

    [Fact]
    public async Task Already_owned_plan_returns_isOwned_true()
    {
        // Issue #358: FREE-plan claim удалён — используем FULL_ALL + admin-grant вместо.
        Guid planId = await CreatePlanAsync(nameof(PlanTier.FULL_ALL), priceCents: 1_000_00);
        await PublishAsync(planId);

        // Автор выпускает admin-grant юзеру.
        Guid userId = Guid.NewGuid();
        AdminGrantRequest grantRequest = new(userId, planId, null);
        HttpResponseMessage grant = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", grantRequest);
        grant.EnsureSuccessStatusCode();

        // Юзер запрашивает quote на тот же план — должен быть isOwned=true.
        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.True(quote!.IsOwned);
        Assert.Equal(0, quote.CreditCents);
    }

    [Fact]
    public async Task Global_full_grant_marks_other_author_course_plan_owned()
    {
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        AuthenticateAs("platform-author", authorA);
        Guid ownedFullPlanId = await CreatePlanAsync(nameof(PlanTier.FULL_ALL), priceCents: 100_000_00);
        await PublishAsync(ownedFullPlanId);
        await SeedGrantAsync(ownedFullPlanId, userId, PlanGrantSource.ADMIN_GRANT);

        AuthenticateAs("platform-author", authorB);
        Guid otherCoursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 20_000_00,
            courseId: Guid.NewGuid());
        await PublishAsync(otherCoursePlanId);

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{otherCoursePlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;

        Assert.NotNull(quote);
        Assert.True(quote!.IsOwned);
        Assert.Equal(0, quote.CreditCents);
    }

    [Fact]
    public async Task Cross_author_course_grant_credits_global_full_plan()
    {
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        AuthenticateAs("platform-author", authorA);
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 50_000_00,
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);
        await SeedGrantAsync(
            coursePlanId,
            userId,
            PlanGrantSource.PURCHASE,
            pricePaidCents: 50_000_00);

        AuthenticateAs("platform-author", authorB);
        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00);
        await PublishAsync(fullPlanId);

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;

        Assert.NotNull(quote);
        Assert.Equal(50_000_00, quote!.CreditCents);
        Assert.Equal(50_000_00, quote.FinalPriceCents);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(coursePlanId, source.PlanId);
    }

    [Fact]
    public async Task Unpaid_active_grant_credits_current_plan_price()
    {
        // #486: grant без оплаты (инвайт / Telegram-бот / GitHub-org) кредитует ТЕКУЩУЮ
        // эффективную цену покрытого плана — юзер уже владеет scope'ом и доплачивает разницу.
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 9_900_00,
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);

        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00);
        await PublishAsync(fullPlanId);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.INVITE_LINK,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: null);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(9_900_00, quote!.CreditCents);
        Assert.Equal(100_000_00 - 9_900_00, quote.FinalPriceCents);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(coursePlanId, source.PlanId);
        Assert.Equal(9_900_00, source.CreditCents);
    }

    [Fact]
    public async Task Unpaid_expired_grant_contributes_no_credit()
    {
        // Истёкший неоплаченный grant ничем не владеет — fallback-credit не применяется
        // (в отличие от истёкшего ОПЛАЧЕННОГО — деньги были внесены).
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 9_900_00,
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);

        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00);
        await PublishAsync(fullPlanId);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.INVITE_LINK,
                sourceRef: Guid.NewGuid(),
                expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
                pricePaidCents: null);
            grant.Expire();
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(0, quote!.CreditCents);
        Assert.Equal(100_000_00, quote.FinalPriceCents);
        Assert.Empty(quote.Sources);
    }

    [Fact]
    public async Task Paid_and_unpaid_grants_on_same_plan_credit_once()
    {
        // Анти-double-count (#486): пара grants на один план — истёкший ОПЛАЧЕННЫЙ
        // (деньги внесены, credit остаётся) + ACTIVE от бота (unique-index
        // uq_plan_grants_user_plan_active не даст второй ACTIVE) — кредитует план
        // ОДИН раз, фактически внесённой суммой (не цена + paid).
        Guid coursePlanId = await CreatePlanAsync(
            nameof(PlanTier.COURSE),
            priceCents: 9_900_00,
            courseId: Guid.NewGuid());
        await PublishAsync(coursePlanId);

        Guid fullPlanId = await CreatePlanAsync(
            nameof(PlanTier.FULL_ALL),
            priceCents: 100_000_00);
        await PublishAsync(fullPlanId);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant paidExpired = PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.PURCHASE,
                sourceRef: Guid.NewGuid(),
                expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
                pricePaidCents: 7_920_00);
            paidExpired.Expire();
            db.PlanGrants.Add(paidExpired);
            db.PlanGrants.Add(PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.INVITE_LINK,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: null));
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{fullPlanId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();

        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        Assert.Equal(7_920_00, quote!.CreditCents);
        Assert.Equal(100_000_00 - 7_920_00, quote.FinalPriceCents);
        Assert.Single(quote.Sources);
    }

    private async Task<Guid> CreatePlanAsync(
        string tier,
        int? priceCents = null,
        Guid? courseId = null)
    {
        CreatePlanRequest req = new(
            Tier: tier,
            Slug: $"upgrade-quote-test-{Guid.NewGuid():N}",
            DisplayName: "Plan",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: courseId is { } __cc ? [__cc] : [],
            DisplayOrder: 0);
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }

    private async Task PublishAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        resp.EnsureSuccessStatusCode();
    }

    private async Task SeedGrantAsync(
        Guid planId,
        Guid userId,
        PlanGrantSource source,
        long? pricePaidCents = null)
    {
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                userId,
                planId,
                source,
                sourceRef: source == PlanGrantSource.PURCHASE ? Guid.NewGuid() : null,
                expiresAt: null,
                pricePaidCents: pricePaidCents);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }
}
