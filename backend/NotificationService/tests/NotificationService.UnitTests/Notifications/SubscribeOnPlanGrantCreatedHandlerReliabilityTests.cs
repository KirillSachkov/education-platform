using System.Linq.Expressions;
using AccessService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Notifications.Handlers;
using NotificationService.Domain.Subscriptions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace NotificationService.UnitTests.Notifications;

public sealed class SubscribeOnPlanGrantCreatedHandlerReliabilityTests
{
    [Fact]
    public async Task Course_bundle_should_check_existing_subscriptions_in_one_query()
    {
        ISubscriptionsRepository subscriptions = Substitute.For<ISubscriptionsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        subscriptions.ListBy(
                Arg.Any<Expression<Func<Subscription, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());

        var sut = new SubscribeOnPlanGrantCreatedHandler(
            Substitute.For<IAccessServiceClient>(),
            subscriptions,
            transactions,
            Substitute.For<ILogger<SubscribeOnPlanGrantCreatedHandler>>());

        PlanGrantCreated message = CreateCourseGrant(
            [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()]);

        await sut.Handle(message, CancellationToken.None);

        await subscriptions.Received(1).ListBy(
            Arg.Any<Expression<Func<Subscription, bool>>>(),
            Arg.Any<CancellationToken>());
        await subscriptions.DidNotReceiveWithAnyArgs().ExistsBy(default!, default);
    }

    [Fact]
    public async Task Save_failure_should_escape_for_wolverine_retry()
    {
        ISubscriptionsRepository subscriptions = Substitute.For<ISubscriptionsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        subscriptions.ListBy(
                Arg.Any<Expression<Func<Subscription, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());

        var sut = new SubscribeOnPlanGrantCreatedHandler(
            Substitute.For<IAccessServiceClient>(),
            subscriptions,
            transactions,
            Substitute.For<ILogger<SubscribeOnPlanGrantCreatedHandler>>());

        PlanGrantCreated message = CreateCourseGrant([Guid.CreateVersion7()]);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.Handle(message, CancellationToken.None));
    }

    private static PlanGrantCreated CreateCourseGrant(IReadOnlyList<Guid> courseIds) => new(
        GrantId: Guid.CreateVersion7(),
        UserId: Guid.CreateVersion7(),
        PlanId: Guid.CreateVersion7(),
        PlanTier: "COURSE",
        PlanAuthorId: Guid.CreateVersion7(),
        CourseId: courseIds[0],
        IncludesFutureContent: false,
        Source: "PAYMENT",
        SourceRef: null,
        GrantedAt: DateTimeOffset.UtcNow,
        ExpiresAt: null,
        CourseIds: courseIds,
        PlanName: "Course plan");
}
