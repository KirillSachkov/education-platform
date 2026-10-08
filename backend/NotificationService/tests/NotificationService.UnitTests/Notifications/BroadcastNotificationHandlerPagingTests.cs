using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Contracts.Broadcast.Requests;
using NotificationService.Core.Database;
using NotificationService.Core.Features.Broadcast.UseCases;
using NotificationService.Domain.Subscriptions;
using NSubstitute;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.UnitTests.Notifications;

public sealed class BroadcastNotificationHandlerPagingTests
{
    [Fact]
    public async Task Handle_UsesCountQuery_WithoutMaterializingSubscriberPage()
    {
        ISubscribersQuery subscribers = Substitute.For<ISubscribersQuery>();
        IOutboxService outbox = Substitute.For<IOutboxService>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        var user = new UserScopedData();
        user.Authenticate(
            Guid.NewGuid(),
            "admin",
            "admin@example.com",
            [PlatformRoles.ADMIN]);

        Guid courseId = Guid.NewGuid();
        subscribers.CountByEntityAsync(
                SubscriptionEntityType.COURSE,
                courseId,
                Arg.Any<CancellationToken>())
            .Returns(12_345);
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());

        var handler = new BroadcastNotificationHandler(
            Substitute.For<IEducationContentServiceClient>(),
            subscribers,
            outbox,
            transactions,
            new BroadcastNotificationValidator(),
            user,
            NullLogger<BroadcastNotificationHandler>.Instance);

        Result<BroadcastNotificationResponse, Error> result = await handler.Handle(
            new BroadcastNotificationCommand(new BroadcastNotificationRequest(
                TargetType: SubscriptionEntityType.COURSE,
                TargetId: courseId,
                Title: "Title",
                Body: "Body",
                Channels: null)),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(12_345, result.Value.EstimatedRecipients);
        await subscribers.Received(1).CountByEntityAsync(
            SubscriptionEntityType.COURSE,
            courseId,
            Arg.Any<CancellationToken>());
        await subscribers.DidNotReceiveWithAnyArgs().ByEntityPageAsync(
            default!,
            Guid.Empty,
            null,
            0,
            CancellationToken.None);
    }
}
