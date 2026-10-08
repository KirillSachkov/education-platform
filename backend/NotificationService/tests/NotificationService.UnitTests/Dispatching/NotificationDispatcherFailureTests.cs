using System.Diagnostics.Metrics;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Core.Channels;
using NotificationService.Core.Channels.InApp;
using NotificationService.Core.Database;
using NotificationService.Core.Diagnostics;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Sse;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NSubstitute;
using SharedKernel;

namespace NotificationService.UnitTests.Dispatching;

public sealed class NotificationDispatcherFailureTests
{
    [Fact]
    public async Task Dispatch_should_propagate_commit_failure_for_message_retry()
    {
        INotificationsRepository notifications = Substitute.For<INotificationsRepository>();
        IUserChannelsRepository userChannels = Substitute.For<IUserChannelsRepository>();
        IUserOptOutsRepository userOptOuts = Substitute.For<IUserOptOutsRepository>();
        IDeliveriesRepository deliveries = Substitute.For<IDeliveriesRepository>();
        IWebPushSubscriptionsRepository webPush = Substitute.For<IWebPushSubscriptionsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        IOutboxService outbox = Substitute.For<IOutboxService>();
        IAuthServiceClient authClient = Substitute.For<IAuthServiceClient>();

        userChannels.GetBulkAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserNotificationChannels>());
        userOptOuts.GetOptedOutBulkAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlySet<NotificationType>>());
        transactions.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        transactions.CommitTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());

        ISseConnectionHub sseHub = Substitute.For<ISseConnectionHub>();
        var channels = new ChannelRegistry([new InAppNotificationChannel(sseHub)]);
        var renderers = new ChannelRendererRegistry([new InAppRenderer()]);

        using var meterFactory = new TestMeterFactory();

        var sut = new NotificationDispatcher(
            notifications,
            userChannels,
            userOptOuts,
            deliveries,
            webPush,
            channels,
            renderers,
            transactions,
            outbox,
            authClient,
            Options.Create(new NotificationOptions()),
            new NotificationMetrics(meterFactory),
            Substitute.For<ILogger<NotificationDispatcher>>());

        NotificationRequest request = NotificationRequest.From(
            NotificationTemplates.LinkAccountsNudge,
            Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7());

        await Assert.ThrowsAnyAsync<Exception>(() => sut.DispatchAsync([request]));
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
                meter.Dispose();
        }
    }
}
