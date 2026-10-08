using AccessService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Notifications.Handlers;
using NotificationService.Domain.Subscriptions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace NotificationService.UnitTests.Notifications;

public sealed class SubscribeLifetimeGranteesReliabilityTests
{
    [Fact]
    public async Task Access_service_failure_should_escape_for_wolverine_retry()
    {
        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        Guid authorId = Guid.CreateVersion7();
        accessClient.GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<Guid>, Error>(
                Error.Failure("access.unavailable", "Access service down")));

        var sut = CreateSut(accessClient, out _, out _);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.Handle(
            new CourseCreated(Guid.CreateVersion7(), authorId),
            CancellationToken.None));
    }

    [Fact]
    public async Task Save_failure_should_escape_for_wolverine_retry()
    {
        IAccessServiceClient accessClient = Substitute.For<IAccessServiceClient>();
        ISubscriptionsRepository subscriptions = Substitute.For<ISubscriptionsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        Guid authorId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        accessClient.GetLifetimeGranteeUserIdsAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<Guid>, Error>([userId]));
        subscriptions.GetSubscribedUserIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                SubscriptionEntityType.COURSE,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());

        var sut = new SubscribeLifetimeGranteesOnCourseCreatedHandler(
            accessClient,
            subscriptions,
            transactions,
            Substitute.For<ILogger<SubscribeLifetimeGranteesOnCourseCreatedHandler>>());

        await Assert.ThrowsAnyAsync<Exception>(() => sut.Handle(
            new CourseCreated(Guid.CreateVersion7(), authorId),
            CancellationToken.None));
    }

    private static SubscribeLifetimeGranteesOnCourseCreatedHandler CreateSut(
        IAccessServiceClient accessClient,
        out ISubscriptionsRepository subscriptions,
        out ITransactionManager transactions)
    {
        subscriptions = Substitute.For<ISubscriptionsRepository>();
        transactions = Substitute.For<ITransactionManager>();

        return new SubscribeLifetimeGranteesOnCourseCreatedHandler(
            accessClient,
            subscriptions,
            transactions,
            Substitute.For<ILogger<SubscribeLifetimeGranteesOnCourseCreatedHandler>>());
    }
}
