using System.Net;
using System.Net.Http.Json;
using NotificationService.Contracts.Admin.Dtos;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Admin;

/// <summary>
///     C.7 admin delivery audit panel — проверяем:
///     <list type="bullet">
///         <item>GET /admin/notifications/deliveries — list с фильтрами + cursor pagination</item>
///         <item>GET /admin/notifications/stats — агрегация per-channel, per-type, top-failures</item>
///         <item>Оба endpoint'а закрыты permission Platform.ADMIN (обычный user → 403)</item>
///     </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class DeliveryAuditTests : NotificationServiceTestsBase
{
    public DeliveryAuditTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ListDeliveries_NonAdmin_Forbidden()
    {
        AuthenticateAs(Guid.NewGuid()); // обычный user без Admin permission
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            new Uri("/admin/notifications/deliveries", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListDeliveries_Admin_ReturnsFilteredByChannel()
    {
        Guid userId = Guid.NewGuid();
        (Guid nId, Guid _) = await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Delivered);
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Telegram, DeliveryStatus.Delivered);

        AuthenticateAsAdmin();
        DeliveryListResponse resp = await FetchAsync<DeliveryListResponse>(
            "/admin/notifications/deliveries?channel=4");  // Email = 4

        Assert.Single(resp.Items);
        Assert.Equal((short)NotificationChannel.Email, resp.Items[0].Channel);
        Assert.Equal(nId, resp.Items[0].NotificationId);
    }

    [Fact]
    public async Task ListDeliveries_Admin_FilterByStatus()
    {
        Guid userId = Guid.NewGuid();
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Delivered);
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Failed);
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Skipped);

        AuthenticateAsAdmin();
        DeliveryListResponse failed = await FetchAsync<DeliveryListResponse>(
            "/admin/notifications/deliveries?status=2");  // Failed = 2

        Assert.Single(failed.Items);
        Assert.Equal((short)DeliveryStatus.Failed, failed.Items[0].Status);
    }

    [Fact]
    public async Task Stats_Admin_ReturnsAggregates()
    {
        Guid userId = Guid.NewGuid();
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Delivered);
        await SeedDeliveryAsync(userId, NotificationType.Welcome, NotificationChannel.Email, DeliveryStatus.Delivered);
        await SeedDeliveryAsync(userId, NotificationType.CourseEnrolled, NotificationChannel.Telegram, DeliveryStatus.Failed, errorCode: "provider.timeout");

        AuthenticateAsAdmin();
        DeliveryStatsResponse stats = await FetchAsync<DeliveryStatsResponse>(
            "/admin/notifications/stats");

        // Email/Delivered = 2, Telegram/Failed = 1
        Assert.Contains(stats.PerChannel, b => b.Channel == (short)NotificationChannel.Email && b.Status == (short)DeliveryStatus.Delivered && b.Count == 2);
        Assert.Contains(stats.PerChannel, b => b.Channel == (short)NotificationChannel.Telegram && b.Status == (short)DeliveryStatus.Failed && b.Count == 1);

        // Types: Welcome = 2, CourseEnrolled = 1
        Assert.Contains(stats.PerType, t => t.Type == (short)NotificationType.Welcome && t.Count == 2);
        Assert.Contains(stats.PerType, t => t.Type == (short)NotificationType.CourseEnrolled && t.Count == 1);

        // Top failures: provider.timeout = 1
        Assert.Contains(stats.TopFailures, f => f.ErrorCode == "provider.timeout" && f.Count == 1);
    }

    [Fact]
    public async Task Stats_Admin_RejectsDateRangeWiderThan90Days()
    {
        // Issue #236 NEW-2: guard, который был в ListDeliveries, скопирован сюда,
        // чтобы тот же admin endpoint не открывал full-table scan через
        // `dateFrom=MinValue` без `dateTo`. Код ошибки одинаков с ListDeliveries.
        AuthenticateAsAdmin();

        DateTimeOffset to = DateTimeOffset.UtcNow;
        DateTimeOffset from = to.AddDays(-91);   // 91 day > MAX_DATE_RANGE_DAYS (90)

        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(
            $"/admin/notifications/stats?dateFrom={Uri.EscapeDataString(from.ToString("O"))}&dateTo={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        SharedKernel.Envelope? envelope = await resp.Content.ReadFromJsonAsync<SharedKernel.Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m =>
            string.Equals(m.Code, "admin.deliveries.date_range_too_wide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stats_Admin_Accepts90DayRange()
    {
        // Граничный кейс — ровно 90 дней должно проходить.
        AuthenticateAsAdmin();

        DateTimeOffset to = DateTimeOffset.UtcNow;
        DateTimeOffset from = to.AddDays(-90);

        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(
            $"/admin/notifications/stats?dateFrom={Uri.EscapeDataString(from.ToString("O"))}&dateTo={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Theory]
    [InlineData("/admin/notifications/deliveries")]
    [InlineData("/admin/notifications/stats")]
    public async Task Admin_delivery_queries_reject_reversed_date_range(string endpoint)
    {
        AuthenticateAsAdmin();
        DateTimeOffset from = DateTimeOffset.UtcNow;
        DateTimeOffset to = from.AddDays(-1);

        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(
            $"{endpoint}?dateFrom={Uri.EscapeDataString(from.ToString("O"))}&dateTo={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ListDeliveries_rejects_unbounded_date_from()
    {
        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(
            $"/admin/notifications/deliveries?dateFrom={Uri.EscapeDataString(DateTimeOffset.MinValue.ToString("O"))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ListDeliveries_rejects_partial_cursor()
    {
        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(
            $"/admin/notifications/deliveries?cursorBefore={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // -- helpers --

    private async Task<(Guid notificationId, Guid deliveryId)> SeedDeliveryAsync(
        Guid userId,
        NotificationType type,
        NotificationChannel channel,
        DeliveryStatus status,
        string? errorCode = null)
    {
        Notification n = Notification.Create(
            recipientUserId: userId,
            type: type,
            templateId: type.ToString().ToLowerInvariant(),
            title: "t", body: "b",
            channels: channel,
            payload: "{}",
            correlationId: Guid.NewGuid()).Value;

        NotificationDelivery d = NotificationDelivery.Create(n.Id, channel).Value;
        if (status == DeliveryStatus.Delivered) d.MarkDelivered(providerMessageId: "p-" + Guid.NewGuid());
        else if (status == DeliveryStatus.Failed) d.MarkFailed(errorCode ?? "generic", "test failure");
        else if (status == DeliveryStatus.Skipped) d.MarkSkipped("test skip");

        await ExecuteInDb(async db =>
        {
            await db.Notifications.AddAsync(n);
            await db.NotificationDeliveries.AddAsync(d);
            await db.SaveChangesAsync();
        });
        return (n.Id.Value, d.Id.Value);
    }

    private async Task<T> FetchAsync<T>(string relativeUrl)
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(relativeUrl, UriKind.Relative));
        resp.EnsureSuccessStatusCode();
        Envelope<T> env = (await resp.Content.ReadFromJsonAsync<Envelope<T>>())!;
        Assert.False(env.IsError, $"Envelope error: {env.Error?.Code}");
        return env.Result!;
    }

    private sealed class Envelope<T>
    {
        public T? Result { get; init; }
        public ErrorEnv? Error { get; init; }
        public bool IsError { get; init; }
    }
    private sealed class ErrorEnv
    {
        public string? Code { get; init; }
        public string? Message { get; init; }
    }
}
