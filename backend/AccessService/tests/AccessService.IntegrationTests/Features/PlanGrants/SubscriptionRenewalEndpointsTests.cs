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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeRenewal_SalesClosed_PreservesExistingGrantAndPublishesNothing(bool cancelled)
    {
        Guid userId = Guid.NewGuid();
        (_, PlanGrant grant) = await SeedRecurringGrantAsync(userId, DateTimeOffset.UtcNow.AddDays(30), cancelled);
        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/me/grants/{grant.Id}/resume-renewal/", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(grant.ExpiresAt!.Value, persisted.ExpiresAt!.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(cancelled, persisted.AutoRenewalCancelledAt is not null);
            Assert.Equal(grant.NextChargeAt, persisted.NextChargeAt);
        });
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalResumed>());
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
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