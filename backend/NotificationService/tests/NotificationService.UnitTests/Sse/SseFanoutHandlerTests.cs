using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Diagnostics;
using NotificationService.Core.Messaging;
using NotificationService.Core.Sse;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace NotificationService.UnitTests.Sse;

/// <summary>
/// Верифицируем dispatch-логику <see cref="SseFanoutHandler"/>:
/// <list type="bullet">
///   <item>Redis publisher disabled → push в локальный hub.</item>
///   <item>Redis publisher enabled → publish в Redis, НЕ push в локальный hub (иначе дубль).</item>
/// </list>
/// </summary>
public sealed class SseFanoutHandlerTests
{
    [Fact]
    public async Task SingleReplica_PushesToLocalHub()
    {
        ISseConnectionHub hub = Substitute.For<ISseConnectionHub>();
        ISseRedisPublisher publisher = Substitute.For<ISseRedisPublisher>();
        publisher.IsEnabled.Returns(false);

        SseFanoutHandler handler = new();
        NotificationCreated evt = MakeEvent();

        await handler.Handle(evt, hub, publisher, BuildMetrics(), CancellationToken.None);

        await hub.Received(1).PushAsync(
            evt.RecipientUserId,
            "notification.created",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await publisher.DidNotReceive().PublishAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultiReplica_PublishesToRedis_DoesNotPushLocally()
    {
        ISseConnectionHub hub = Substitute.For<ISseConnectionHub>();
        ISseRedisPublisher publisher = Substitute.For<ISseRedisPublisher>();
        publisher.IsEnabled.Returns(true);

        SseFanoutHandler handler = new();
        NotificationCreated evt = MakeEvent();

        await handler.Handle(evt, hub, publisher, BuildMetrics(), CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            evt.RecipientUserId,
            "notification.created",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await hub.DidNotReceive().PushAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static NotificationMetrics BuildMetrics()
    {
        IMeterFactory factory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        return new NotificationMetrics(factory);
    }

    private static NotificationCreated MakeEvent() =>
        new(
            NotificationId: Guid.NewGuid(),
            RecipientUserId: Guid.NewGuid(),
            Type: 1,
            Channels: 1,
            TemplateId: "welcome",
            Title: "Title",
            Body: "Body",
            TelegramBody: null,
            PayloadJson: "{}",
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow);
}
