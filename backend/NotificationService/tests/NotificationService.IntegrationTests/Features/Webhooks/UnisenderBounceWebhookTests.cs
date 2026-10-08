using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Database;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Webhooks;

/// <summary>
///     <c>POST /webhooks/unisender</c> обрабатывает статус-события:
///     <list type="bullet">
///         <item><c>hard_bounced</c> / <c>spam</c> / <c>unsubscribed</c>:
///               delivery → Failed + EmailEnabled=false у recipient'а.</item>
///         <item><c>soft_bounced</c>: delivery → Failed, но email канал оставляем.</item>
///         <item><c>delivered</c>: no-op (positive status).</item>
///     </list>
///     <para>
///     Endpoint fail-closed (#430): анонимный мутирующий webhook требует совпадающий
///     <c>X-Unisender-Secret</c> header. Happy-path тесты шлют секрет, заданный фабрикой
///     (<see cref="IntegrationTestsWebFactory.UNISENDER_WEBHOOK_SECRET"/>); регрессии на
///     отсутствие секрета / неверный header проверяют 401.
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class UnisenderBounceWebhookTests : NotificationServiceTestsBase
{
    private const string SECRET_HEADER = "X-Unisender-Secret";

    public UnisenderBounceWebhookTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task HardBounce_MarksFailed_DisablesEmail()
    {
        Guid userId = Guid.NewGuid();
        (_, _, string jobId) = await SeedPendingEmailDeliveryAsync(userId);
        await SetChannelsAsync(userId, telegramEnabled: true, emailEnabled: true);

        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload(jobId, "hard_bounced", email: "bad@example.com"),
            secret: IntegrationTestsWebFactory.UNISENDER_WEBHOOK_SECRET);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Delivery → Failed
        NotificationDelivery updated = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking().SingleAsync(d => d.ProviderMessageId == jobId));
        Assert.Equal(DeliveryStatus.Failed, updated.Status);
        Assert.Equal("hard_bounced", updated.ErrorCode);

        // Email disabled, Telegram остался
        UserNotificationChannels? channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId));
        Assert.NotNull(channels);
        Assert.False(channels!.EmailEnabled);
        Assert.True(channels.TelegramEnabled);
    }

    [Fact]
    public async Task SoftBounce_MarksFailed_KeepsEmailEnabled()
    {
        Guid userId = Guid.NewGuid();
        (_, _, string jobId) = await SeedPendingEmailDeliveryAsync(userId);
        await SetChannelsAsync(userId, telegramEnabled: false, emailEnabled: true);

        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload(jobId, "soft_bounced", email: "slow@example.com"),
            secret: IntegrationTestsWebFactory.UNISENDER_WEBHOOK_SECRET);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        NotificationDelivery updated = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking().SingleAsync(d => d.ProviderMessageId == jobId));
        Assert.Equal(DeliveryStatus.Failed, updated.Status);

        UserNotificationChannels? channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId));
        // Email НЕ выключен — soft bounce не permanent
        Assert.True(channels?.EmailEnabled ?? true);
    }

    [Fact]
    public async Task DeliveredStatus_NoOp()
    {
        Guid userId = Guid.NewGuid();
        (_, _, string jobId) = await SeedPendingEmailDeliveryAsync(userId);

        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload(jobId, "delivered"),
            secret: IntegrationTestsWebFactory.UNISENDER_WEBHOOK_SECRET);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Status не изменился — delivery всё ещё Pending.
        NotificationDelivery updated = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking().SingleAsync(d => d.ProviderMessageId == jobId));
        Assert.Equal(DeliveryStatus.Pending, updated.Status);
    }

    [Fact]
    public async Task UnknownJobId_OK_NoOp()
    {
        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload("non-existent-id", "hard_bounced", email: "x@y.z"),
            secret: IntegrationTestsWebFactory.UNISENDER_WEBHOOK_SECRET);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // -- fail-closed regression (#430) --

    [Fact]
    public async Task SecretConfigured_MissingHeader_Returns401()
    {
        Guid userId = Guid.NewGuid();
        (_, _, string jobId) = await SeedPendingEmailDeliveryAsync(userId);
        await SetChannelsAsync(userId, telegramEnabled: true, emailEnabled: true);

        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload(jobId, "hard_bounced", email: "bad@example.com"),
            secret: null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        // Spoof'нутый bounce без секрета не должен ничего поменять — delivery остался Pending.
        NotificationDelivery delivery = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking().SingleAsync(d => d.ProviderMessageId == jobId));
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);

        // Email-канал не тронут.
        UserNotificationChannels? channels = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId));
        Assert.True(channels!.EmailEnabled);
    }

    [Fact]
    public async Task SecretConfigured_WrongHeader_Returns401()
    {
        Guid userId = Guid.NewGuid();
        (_, _, string jobId) = await SeedPendingEmailDeliveryAsync(userId);
        await SetChannelsAsync(userId, telegramEnabled: true, emailEnabled: true);

        RemoveAuthentication();
        HttpResponseMessage resp = await PostWebhookAsync(
            BuildPayload(jobId, "hard_bounced", email: "bad@example.com"),
            secret: "definitely-not-the-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        // Неверный секрет → no-op, delivery остался Pending.
        NotificationDelivery delivery = await ExecuteInDb(db => db.NotificationDeliveries
            .AsNoTracking().SingleAsync(d => d.ProviderMessageId == jobId));
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
    }

    /// <summary>
    ///     Когда <c>Notifications:Unisender:WebhookSecret</c> вообще не задан, fail-closed
    ///     отклоняет даже корректный payload с любым (или без) header'ом. Поднимает отдельный
    ///     host без секрета (<see cref="NoSecretIntegrationTestsWebFactory"/>).
    /// </summary>
    [Fact]
    public async Task NoSecretConfigured_Returns401()
    {
        // Не используем `await using`: фабрика прячет базовый DisposeAsync через `new`
        // (xUnit IAsyncLifetime.DisposeAsync : Task, базовый IAsyncDisposable : ValueTask).
        // Зовём наш DisposeAsync явно в finally — он останавливает Postgres-контейнер.
        NoSecretIntegrationTestsWebFactory factory = new();
        await factory.InitializeAsync();
        try
        {
            using HttpClient client = factory.CreateClient();

            HttpRequestMessage request = new(
                HttpMethod.Post, new Uri("/webhooks/unisender", UriKind.Relative))
            {
                Content = JsonContent.Create(BuildPayload("job-irrelevant", "hard_bounced", email: "x@y.z")),
            };
            // Даже если внешний провайдер прислал бы header — без конфиг-секрета endpoint закрыт.
            request.Headers.Add(SECRET_HEADER, "anything");

            HttpResponseMessage resp = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -- helpers --

    private Task<HttpResponseMessage> PostWebhookAsync(object payload, string? secret)
    {
        HttpRequestMessage request = new(
            HttpMethod.Post, new Uri("/webhooks/unisender", UriKind.Relative))
        {
            Content = JsonContent.Create(payload),
        };

        if (secret is not null)
            request.Headers.Add(SECRET_HEADER, secret);

        return AppHttpClient.SendAsync(request);
    }

    private async Task<(Guid notificationId, Guid deliveryId, string providerMessageId)>
        SeedPendingEmailDeliveryAsync(Guid userId)
    {
        string providerMessageId = "job-" + Guid.NewGuid();
        Notification n = Notification.Create(
            recipientUserId: userId,
            type: NotificationType.Welcome,
            templateId: "welcome",
            title: "t", body: "b",
            channels: NotificationChannel.Email,
            payload: "{}",
            correlationId: Guid.NewGuid()).Value;

        NotificationDelivery d = NotificationDelivery.Create(n.Id, NotificationChannel.Email).Value;
        // Для теста напрямую прописываем provider_message_id в БД (через raw SQL),
        // т.к. domain-модель держит его immutable через MarkDelivered (который поменяет status).
        await ExecuteInDb(async db =>
        {
            await db.Notifications.AddAsync(n);
            await db.NotificationDeliveries.AddAsync(d);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE notifications.notification_deliveries
                 SET provider_message_id = {providerMessageId}
                 WHERE id = {d.Id.Value}
                 """);
        });

        return (n.Id.Value, d.Id.Value, providerMessageId);
    }

    private async Task SetChannelsAsync(Guid userId, bool telegramEnabled, bool emailEnabled)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IUserChannelsRepository repo = scope.ServiceProvider.GetRequiredService<IUserChannelsRepository>();
        await repo.UpsertFlagsAsync(userId, telegramEnabled, emailEnabled, webPushEnabled: true);
    }

    private static object BuildPayload(string jobId, string status, string? email = null) => new
    {
        events_by_user = new[]
        {
            new
            {
                user_id = 1L,
                events = new[]
                {
                    new
                    {
                        event_name = "transactional_email_status",
                        event_data = new
                        {
                            job_id = jobId,
                            status,
                            email,
                            delivery_info = new { destination_response = "(test)" },
                        },
                    },
                },
            },
        },
    };
}
