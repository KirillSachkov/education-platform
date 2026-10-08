using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     Reset onboarding endpoint. Покрывает:
///     - стандартный путь (existing onboarding row → reset на первый шаг);
///     - self-heal для legacy grant'ов без onboarding row (выпущенных до issue #68);
///     - guard: без active grant запрещаем создавать строку из ниоткуда;
///     - flow.IsEnabled=false → FlowDisabled (не self-heal в gate-петлю).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ResetOnboardingTests : AccessServiceTestsBase
{
    public ResetOnboardingTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Resets_existing_onboarding_to_first_step()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        // Симулируем юзера, который уже прошёл часть wizard'а: руками создаём
        // onboarding row с current_step_id=null + skipped/completed.
        Guid userId = AccessServiceTestsBase.DefaultUserId;
        Guid firstStepId = await ExecuteInDbAsync(async db =>
        {
            Guid stepId = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .Select(s => s.Id)
                .FirstAsync();

            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.SetCurrentStep(null); // якобы завершил всё
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
            return stepId;
        });

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/reset/", content: null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == userId && o.PlanId == planId);
            Assert.Null(ob.CompletedAt);
            Assert.Equal(firstStepId, ob.CurrentStepId);
            Assert.Empty(ob.CompletedStepIds);
            Assert.Empty(ob.SkippedStepIds);
        });
    }

    [Fact]
    public async Task Self_heals_when_legacy_grant_has_no_onboarding_row()
    {
        // Симуляция legacy grant'а: PlanGrant в БД, но user_plan_onboardings отсутствует.
        // В проде так выглядят все grant'ы, выданные до 2026-05-06 (создание handler'а).
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);
        await SeedActiveGrantAsync(studentId, planId);

        // Зеркалим продакшн-логику: грузим в память и сортируем StringComparer.Ordinal.
        // EF не транслирует StringComparer, а Postgres collation может отличаться от
        // ordinal на edge-кейсах (case-sensitivity для base-62 ключей).
        Guid expectedFirstStep = await ExecuteInDbAsync(async db =>
        {
            List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .ToListAsync();
            return steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id)
                .First();
        });

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/reset/", content: null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Equal(expectedFirstStep, ob.CurrentStepId);
            Assert.Null(ob.CompletedAt);
        });
    }

    [Fact]
    public async Task Returns_not_found_when_no_grant_and_no_onboarding()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid randomUserId = Guid.NewGuid();
        AuthenticateAs("platform-participant", randomUserId);

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/reset/", content: null);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.UserPlanOnboardings.CountAsync(o => o.UserId == randomUserId);
            Assert.Equal(0, count); // не создаём строку без grant'а
        });
    }

    [Fact]
    public async Task Returns_flow_disabled_when_flow_off()
    {
        Guid planId = await CreatePlanAsync();

        // Включаем чтобы plan_onboarding_flows row создалась + появились шаги,
        // потом выключаем — это imitirует prod-сценарий «автор передумал». Только
        // disabled-with-row даёт FlowDisabled (Validation/400); если row нет вообще
        // — handler вернёт FlowNotFound (NotFound/404), это другой code, не то что
        // тестируем.
        await EnableOnboardingAsync(planId);
        HttpResponseMessage disable = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: false));
        disable.EnsureSuccessStatusCode();

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);
        await SeedActiveGrantAsync(studentId, planId);

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/reset/", content: null);

        // Error.Validation → 400 BadRequest (см. UpdatePlanTests, ArchivePlanTests
        // для precedent). Защищаемся от регрессии в 500/404.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Backfill_migration_creates_rows_for_legacy_grants()
    {
        // Прямая симуляция backfill SQL, регрессия на критерий «active + flow.is_enabled».
        // После #358 source-фильтр AUTO_FREE из backfill убран — этот source больше не
        // выпускается. Имитирует то, что делает миграция 20260510183548
        // BackfillUserPlanOnboardings на проде: отсеиваем grant'ы по статусу/flow и
        // заполняем недостающие onboarding-row'ы.
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid eligibleUser = Guid.NewGuid();
        Guid revokedUser = Guid.NewGuid();

        await SeedActiveGrantAsync(eligibleUser, planId);
        await SeedRevokedGrantAsync(revokedUser, planId);

        // Запускаем backfill SQL вручную (та же логика, что в Up() миграции).
        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO access.user_plan_onboardings (
                    user_id, plan_id, started_at, completed_at,
                    current_step_id, completed_step_ids, skipped_step_ids
                )
                SELECT DISTINCT
                    g.user_id, g.plan_id, NOW(), NULL::timestamptz,
                    (SELECT s.id FROM access.plan_onboarding_steps s
                     WHERE s.plan_id = g.plan_id ORDER BY s.sort_order ASC LIMIT 1),
                    ARRAY[]::uuid[], ARRAY[]::uuid[]
                FROM access.plan_grants g
                INNER JOIN access.plan_onboarding_flows f ON f.plan_id = g.plan_id
                WHERE g.status = 'ACTIVE'
                  AND f.is_enabled = TRUE
                  AND EXISTS (SELECT 1 FROM access.plan_onboarding_steps s WHERE s.plan_id = g.plan_id)
                  AND NOT EXISTS (
                      SELECT 1 FROM access.user_plan_onboardings o
                      WHERE o.user_id = g.user_id AND o.plan_id = g.plan_id
                  );
                """);
        });

        await ExecuteInDbAsync(async db =>
        {
            Assert.True(await db.UserPlanOnboardings
                .AnyAsync(o => o.UserId == eligibleUser && o.PlanId == planId));
            Assert.False(await db.UserPlanOnboardings
                .AnyAsync(o => o.UserId == revokedUser));
        });
    }

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

    private async Task EnableOnboardingAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        resp.EnsureSuccessStatusCode();
    }

    private async Task SeedActiveGrantAsync(
        Guid userId, Guid planId, PlanGrantSource source = PlanGrantSource.ADMIN_GRANT)
    {
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(userId, planId, source, sourceRef: null);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedRevokedGrantAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(userId, planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
            grant.Revoke(Guid.NewGuid(), "test");
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }
}
