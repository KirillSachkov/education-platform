using System.Net;
using System.Text.Json;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SubscriptionRenewalEndpointsTests : AccessServiceTestsBase
{
    private const long PRICE_CENTS = 99_000;

    public SubscriptionRenewalEndpointsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task CancelRenewal_Owner_PreservesPaidExpiry_ClearsGrace_AndPublishesOnce()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        (Plan plan, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: false,
            dunningFailureCount: 1);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null);
        HttpResponseMessage replay = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(expiresAt, persisted.ExpiresAt!.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(expiresAt, persisted.AccessEndsAt!.Value, TimeSpan.FromMicroseconds(1));
            Assert.NotNull(persisted.AutoRenewalCancelledAt);
            Assert.Null(persisted.NextChargeAt);
            Assert.Null(persisted.RenewalGraceEndsAt);
            Assert.Equal(0, persisted.ChargeFailureCount);
        });

        PlanGrantRenewalCancelled published =
            Assert.Single(OutboxCollector.OfType<PlanGrantRenewalCancelled>());
        Assert.Equal(grant.Id, published.GrantId);
        Assert.Equal(userId, published.UserId);
        Assert.Equal(plan.Id, published.PlanId);
        Assert.Equal(plan.AuthorId, published.PlanAuthorId);
        Assert.Equal(expiresAt, published.AccessEndsAt, TimeSpan.FromMicroseconds(1));
        Assert.Equal(1, published.Attempt);
        Assert.NotEqual(Guid.Empty, published.CorrelationId);
        Assert.Null(published.RenewalOrderId);
        Assert.Equal(SubscriptionLifecycleStages.Cancelled, published.Stage);
    }

    [Fact]
    public async Task ResumeRenewal_Owner_SchedulesCanonicalLeadTime_AndPublishesOnce()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        (Plan plan, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: true);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);
        HttpResponseMessage replay = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        DateTimeOffset expectedChargeAt = SubscriptionRenewalPolicy.FirstChargeAt(expiresAt);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Null(persisted.AutoRenewalCancelledAt);
            Assert.Null(persisted.RenewalGraceEndsAt);
            Assert.Equal(expectedChargeAt, persisted.NextChargeAt!.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(0, persisted.ChargeFailureCount);
        });

        PlanGrantRenewalResumed published =
            Assert.Single(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        Assert.Equal(grant.Id, published.GrantId);
        Assert.Equal(userId, published.UserId);
        Assert.Equal(plan.Id, published.PlanId);
        Assert.Equal(plan.AuthorId, published.PlanAuthorId);
        Assert.Equal(expectedChargeAt, published.NextChargeAt, TimeSpan.FromMicroseconds(1));
        Assert.Equal(
            SubscriptionRenewalPolicy.GraceEndsAt(expiresAt),
            published.GraceEndsAt,
            TimeSpan.FromMicroseconds(1));
        Assert.Equal(0, published.Attempt);
        Assert.NotEqual(Guid.Empty, published.CorrelationId);
        Assert.Null(published.RenewalOrderId);
        Assert.Equal(SubscriptionLifecycleStages.Resumed, published.Stage);
    }

    [Fact]
    public async Task CancelRenewal_DuringGraceAfterPaidExpiry_ExpiresGrantAndPublishesAccessCleanup()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset paidExpiry = DateTimeOffset.UtcNow.AddMinutes(-1);
        (Plan plan, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            paidExpiry,
            cancelled: false,
            dunningFailureCount: 1);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null);
        HttpResponseMessage replay = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await ExecuteInDbAsync(async db => Assert.Equal(
            PlanGrantStatus.EXPIRED,
            (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).Status));

        PlanGrantExpired expired = Assert.Single(OutboxCollector.OfType<PlanGrantExpired>());
        Assert.Equal(grant.Id, expired.GrantId);
        Assert.Equal(plan.Id, expired.PlanId);
        Assert.Equal(userId, expired.UserId);
        Assert.Single(OutboxCollector.OfType<PlanGrantRenewalCancelled>());
    }

    [Theory]
    [InlineData("cancel-renewal")]
    [InlineData("resume-renewal")]
    public async Task RenewalMutation_Anonymous_Returns401(string action)
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{Guid.NewGuid()}/{action}/", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("cancel-renewal")]
    [InlineData("resume-renewal")]
    public async Task RenewalMutation_OtherUser_Returns404(string action)
    {
        Guid ownerId = Guid.NewGuid();
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            ownerId,
            DateTimeOffset.UtcNow.AddDays(30),
            cancelled: true);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/{action}/", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalCancelled>());
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
    }

    [Theory]
    [InlineData("cancel-renewal")]
    [InlineData("resume-renewal")]
    public async Task RenewalMutation_ChargeLockBusy_Returns409WithoutMutation(string action)
    {
        Guid userId = Guid.NewGuid();
        bool initiallyCancelled = string.Equals(action, "resume-renewal", StringComparison.Ordinal);
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            DateTimeOffset.UtcNow.AddDays(30),
            cancelled: initiallyCancelled);
        AuthenticateAs("platform-participant", userId);

        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        IOrdersRepository orders = scope.ServiceProvider.GetRequiredService<IOrdersRepository>();
        await using IAsyncDisposable heldLock = (await orders.TryAcquireRenewalLockAsync(
            grant.Id,
            CancellationToken.None))!;

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/{action}/", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await ExecuteInDbAsync(async db => Assert.Equal(
            initiallyCancelled,
            (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id))
            .AutoRenewalCancelledAt is not null));
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalCancelled>());
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
    }

    [Fact]
    public async Task ResumeRenewal_AfterPaidExpiry_Returns400AndRemainsCancelled()
    {
        Guid userId = Guid.NewGuid();
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            DateTimeOffset.UtcNow.AddDays(1),
            cancelled: true);
        await ExecuteInDbAsync(async db => await db.PlanGrants
            .Where(g => g.Id == grant.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                g => g.ExpiresAt,
                DateTimeOffset.UtcNow.AddMinutes(-1))));
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteInDbAsync(async db => Assert.NotNull(
            (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id))
            .AutoRenewalCancelledAt));
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
    }

    [Fact]
    public async Task ResumeRenewal_AlreadyEnabledWithoutScheduledCharge_IsIdempotentWithoutEvent()
    {
        Guid userId = Guid.NewGuid();
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            DateTimeOffset.UtcNow.AddDays(30),
            cancelled: false);
        await ExecuteInDbAsync(async db => await db.PlanGrants
            .Where(g => g.Id == grant.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                g => g.NextChargeAt,
                (DateTimeOffset?)null)));
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        await ExecuteInDbAsync(async db => Assert.Null(
            (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).NextChargeAt));
    }

    [Fact]
    public async Task ResumeRenewal_ActiveDunningWithScheduledRetry_IsIdempotentWithoutEvent()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: false,
            dunningFailureCount: 1);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(1, persisted.ChargeFailureCount);
            Assert.Equal(expiresAt, persisted.NextChargeAt!.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(
                SubscriptionRenewalPolicy.GraceEndsAt(expiresAt),
                persisted.RenewalGraceEndsAt!.Value,
                TimeSpan.FromMicroseconds(1));
        });
    }

    [Fact]
    public async Task ResumeRenewal_TerminalDunningBeforeGraceEnd_SchedulesImmediateAttemptAndPublishes()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset graceEndsAt = SubscriptionRenewalPolicy.GraceEndsAt(expiresAt);
        (Plan plan, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: false,
            dunningFailureCount: SubscriptionRenewalPolicy.MAX_ATTEMPTS);
        AuthenticateAs("platform-participant", userId);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.ACTIVE, persisted.Status);
            Assert.Null(persisted.AutoRenewalCancelledAt);
            Assert.Equal(SubscriptionRenewalPolicy.MAX_ATTEMPTS - 1, persisted.ChargeFailureCount);
            Assert.InRange(persisted.NextChargeAt!.Value, before, after);
            Assert.Equal(
                graceEndsAt,
                persisted.RenewalGraceEndsAt!.Value,
                TimeSpan.FromMicroseconds(1));
        });

        PlanGrantRenewalResumed published =
            Assert.Single(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        Assert.Equal(grant.Id, published.GrantId);
        Assert.Equal(plan.Id, published.PlanId);
        Assert.InRange(published.NextChargeAt, before, after);
        Assert.Equal(graceEndsAt, published.GraceEndsAt, TimeSpan.FromMicroseconds(1));
        Assert.Equal(SubscriptionRenewalPolicy.MAX_ATTEMPTS - 1, published.Attempt);
        Assert.NotEqual(Guid.Empty, published.CorrelationId);
    }

    [Fact]
    public async Task ResumeRenewal_TerminalDunningAfterGraceEnd_Returns400WithoutMutationOrEvent()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(-73);
        DateTimeOffset graceEndsAt = SubscriptionRenewalPolicy.GraceEndsAt(expiresAt);
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: false,
            dunningFailureCount: SubscriptionRenewalPolicy.MAX_ATTEMPTS);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(SubscriptionRenewalPolicy.MAX_ATTEMPTS, persisted.ChargeFailureCount);
            Assert.Null(persisted.NextChargeAt);
            Assert.Equal(
                graceEndsAt,
                persisted.RenewalGraceEndsAt!.Value,
                TimeSpan.FromMicroseconds(1));
        });
    }

    [Fact]
    public async Task CancelResumeCancel_UsesDistinctCorrelationIdsForEachActualTransition()
    {
        Guid userId = Guid.NewGuid();
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            DateTimeOffset.UtcNow.AddDays(30),
            cancelled: false);
        AuthenticateAs("platform-participant", userId);

        (await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null)).EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null)).EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/cancel-renewal/", content: null)).EnsureSuccessStatusCode();

        PlanGrantRenewalCancelled[] cancelled =
            [.. OutboxCollector.OfType<PlanGrantRenewalCancelled>()];
        PlanGrantRenewalResumed resumed = Assert.Single(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        Assert.Equal(2, cancelled.Length);
        Guid[] correlationIds = [cancelled[0].CorrelationId, resumed.CorrelationId, cancelled[1].CorrelationId];
        Assert.All(correlationIds, correlationId => Assert.NotEqual(Guid.Empty, correlationId));
        Assert.Equal(3, correlationIds.Distinct().Count());
    }

    [Fact]
    public async Task GetMyGrants_ReturnsSubscriptionLifecycleFields()
    {
        Guid userId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(
            userId,
            expiresAt,
            cancelled: false,
            dunningFailureCount: 1);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/me/grants/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement dto = json.RootElement.GetProperty("result")[0];
        Assert.Equal(
            grant.NextChargeAt!.Value,
            dto.GetProperty("nextChargeAt").GetDateTimeOffset(),
            TimeSpan.FromMicroseconds(1));
        Assert.Equal(1, dto.GetProperty("chargeFailureCount").GetInt32());
        Assert.Equal(
            grant.RenewalGraceEndsAt!.Value,
            dto.GetProperty("renewalGraceEndsAt").GetDateTimeOffset(),
            TimeSpan.FromMicroseconds(1));
        Assert.Equal(
            grant.AccessEndsAt!.Value,
            dto.GetProperty("accessEndsAt").GetDateTimeOffset(),
            TimeSpan.FromMicroseconds(1));
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("autoRenewalCancelledAt").ValueKind);
    }

    private async Task<(Plan Plan, PlanGrant Grant)> SeedRecurringGrantAsync(
        Guid userId,
        DateTimeOffset expiresAt,
        bool cancelled,
        int dunningFailureCount = 0)
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.SUBSCRIPTION,
            slug: PlanSlug.Of($"subscription-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Подписка").Value,
            courseIds: [],
            requestedCapabilities: null,
            term: PlanTerm.Recurring(30)).Value;
        plan.UpdatePrice(PRICE_CENTS, "RUB");

        PlanGrant grant = PlanGrant.Create(
            userId,
            plan.Id,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: expiresAt,
            pricePaidCents: PRICE_CENTS);
        Assert.True(grant.AttachRecurring(
            $"rebill-{Guid.NewGuid():N}",
            userId.ToString(),
            SubscriptionRenewalPolicy.FirstChargeAt(expiresAt)).IsSuccess);

        for (int completedAttempts = 1; completedAttempts <= dunningFailureCount; completedAttempts++)
        {
            Assert.True(grant.RecordChargeFailure(
                SubscriptionRenewalPolicy.NextRetryAt(expiresAt, completedAttempts),
                SubscriptionRenewalPolicy.GraceEndsAt(expiresAt)).IsSuccess);
        }

        if (cancelled)
        {
            Assert.True(grant.CancelAutoRenewal(DateTimeOffset.UtcNow.AddMinutes(-1)).IsSuccess);
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        return (plan, grant);
    }
}
