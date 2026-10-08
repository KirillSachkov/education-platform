using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Messaging.Consumers;
using NotificationService.Core.Notifications;
using NotificationService.Domain.Subscriptions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;

namespace NotificationService.UnitTests.Notifications;

public sealed class BroadcastFanoutPagingTests
{
    [Fact]
    public async Task BroadcastFanout_With650Subscribers_ReadsTwoPages_AndDispatchesEveryoneOnce()
    {
        ISubscribersQuery subscribers = Substitute.For<ISubscribersQuery>();
        INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
        IEducationContentServiceClient education = Substitute.For<IEducationContentServiceClient>();
        IAuthServiceClient auth = Substitute.For<IAuthServiceClient>();

        Guid courseId = Guid.NewGuid();
        Guid[] recipients = Enumerable.Range(0, 650)
            .Select(_ => Guid.NewGuid())
            .Order()
            .ToArray();
        Guid firstPageCursor = recipients[499];

        subscribers.ByEntityPageAsync(
                SubscriptionEntityType.COURSE,
                courseId,
                null,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[..500], firstPageCursor));
        subscribers.ByEntityPageAsync(
                SubscriptionEntityType.COURSE,
                courseId,
                firstPageCursor,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[500..], null));

        education.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId,
                Slug: "course",
                Title: "Course",
                Description: "",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: null)));

        List<Guid> dispatchedRecipients = [];
        dispatcher
            .When(x => x.DispatchAsync(
                Arg.Any<IReadOnlyList<NotificationRequest>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => dispatchedRecipients.AddRange(
                call.Arg<IReadOnlyList<NotificationRequest>>().Select(x => x.RecipientUserId)));

        var handler = new BroadcastFanoutHandler(
            subscribers,
            dispatcher,
            education,
            auth,
            NullLogger<BroadcastFanoutHandler>.Instance);

        await handler.Handle(
            new NotificationBroadcastRequested(
                BroadcastId: Guid.NewGuid(),
                RequestedByUserId: Guid.NewGuid(),
                TargetType: SubscriptionEntityType.COURSE,
                TargetId: courseId,
                TemplateId: "author.announcement",
                Title: "Title",
                Body: "Body",
                Channels: 0,
                PayloadJson: "{}",
                RequestedAt: DateTimeOffset.UtcNow),
            default);

        await dispatcher.Received(26).DispatchAsync(
            Arg.Any<IReadOnlyList<NotificationRequest>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(recipients, dispatchedRecipients.Order());
        Assert.Equal(650, dispatchedRecipients.Distinct().Count());
        await subscribers.Received(1).ByEntityPageAsync(
            SubscriptionEntityType.COURSE,
            courseId,
            null,
            500,
            Arg.Any<CancellationToken>());
        await subscribers.Received(1).ByEntityPageAsync(
            SubscriptionEntityType.COURSE,
            courseId,
            firstPageCursor,
            500,
            Arg.Any<CancellationToken>());
    }
}
