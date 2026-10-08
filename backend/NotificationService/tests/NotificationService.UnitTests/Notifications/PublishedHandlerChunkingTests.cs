using AuthService.Contracts.AuthorSpaces;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Notifications.Handlers;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;

namespace NotificationService.UnitTests.Notifications;

/// <summary>
/// `MaterialPublishedHandler` / `IssuePublishedHandler` чанкают recipients по
/// <c>BATCH_SIZE = 25</c> вместо одного гигантского <c>DispatchAsync(N)</c>.
/// Размер был 200, после v1.5.3 (#67) уменьшён до 25 — EF ChangeTracker
/// + outbox buffer удерживали entities в scoped DbContext'е до конца
/// handler-таски, peak memory pressure хитал cgroup limit на NS.
/// </summary>
public sealed class PublishedHandlerChunkingTests
{
    [Fact]
    public async Task MaterialPublished_With650Subscribers_ReadsTwoPages_AndDispatchesIn26Chunks()
    {
        ISubscribersQuery subs = Substitute.For<ISubscribersQuery>();
        INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
        IEducationContentServiceClient ecs = Substitute.For<IEducationContentServiceClient>();
        IAuthServiceClient auth = Substitute.For<IAuthServiceClient>();

        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid[] recipients = Enumerable.Range(0, 650)
            .Select(_ => Guid.NewGuid())
            .Order()
            .ToArray();
        Guid firstPageCursor = recipients[499];

        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                null,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[..500], firstPageCursor));
        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                firstPageCursor,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[500..], null));

        ecs.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId, Slug: "course", Title: "Course",
                Description: "",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        auth.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "author")));

        List<Guid> dispatchedRecipients = [];
        dispatcher
            .When(x => x.DispatchAsync(
                Arg.Any<IReadOnlyList<NotificationRequest>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => dispatchedRecipients.AddRange(
                call.Arg<IReadOnlyList<NotificationRequest>>().Select(x => x.RecipientUserId)));

        var handler = new MaterialPublishedHandler(
            subs, dispatcher, ecs, auth,
            NullLogger<MaterialPublishedHandler>.Instance);

        await handler.Handle(
            new MaterialPublished(
                MaterialId: Guid.NewGuid(),
                Title: "Test material",
                AuthorId: authorId,
                CourseIds: [courseId],
                NotifySubscribers: true),
            default);

        // 650 / 25 = 26 chunks across two bounded subscriber pages.
        await dispatcher.Received(26).DispatchAsync(
            Arg.Any<IReadOnlyList<NotificationRequest>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(recipients, dispatchedRecipients.Order());
        Assert.Equal(650, dispatchedRecipients.Distinct().Count());
        await subs.Received(1).ByEntityPageAsync(
            Arg.Any<string>(), courseId, null, 500, Arg.Any<CancellationToken>());
        await subs.Received(1).ByEntityPageAsync(
            Arg.Any<string>(), courseId, firstPageCursor, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MaterialPublished_With50Subscribers_DispatchesIn2Chunks()
    {
        ISubscribersQuery subs = Substitute.For<ISubscribersQuery>();
        INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
        IEducationContentServiceClient ecs = Substitute.For<IEducationContentServiceClient>();
        IAuthServiceClient auth = Substitute.For<IAuthServiceClient>();

        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        IReadOnlyList<Guid> recipients = Enumerable.Range(0, 50)
            .Select(_ => Guid.NewGuid())
            .ToList();

        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                null,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients, null));

        ecs.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId, Slug: "course", Title: "Course",
                Description: "",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        auth.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "author")));

        var handler = new MaterialPublishedHandler(
            subs, dispatcher, ecs, auth,
            NullLogger<MaterialPublishedHandler>.Instance);

        await handler.Handle(
            new MaterialPublished(
                MaterialId: Guid.NewGuid(),
                Title: "Test material",
                AuthorId: authorId,
                CourseIds: [courseId],
                NotifySubscribers: true),
            default);

        // 50 / 25 = 2 chunks.
        await dispatcher.Received(2).DispatchAsync(
            Arg.Any<IReadOnlyList<NotificationRequest>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssuePublished_With650Subscribers_ReadsTwoPages_AndDispatchesIn26Chunks()
    {
        ISubscribersQuery subs = Substitute.For<ISubscribersQuery>();
        INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
        IEducationContentServiceClient ecs = Substitute.For<IEducationContentServiceClient>();
        IAuthServiceClient auth = Substitute.For<IAuthServiceClient>();

        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid[] recipients = Enumerable.Range(0, 650)
            .Select(_ => Guid.NewGuid())
            .Order()
            .ToArray();
        Guid firstPageCursor = recipients[499];

        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                null,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[..500], firstPageCursor));
        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                firstPageCursor,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients[500..], null));

        ecs.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId, Slug: "course", Title: "Course",
                Description: "",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        auth.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "author")));

        List<Guid> dispatchedRecipients = [];
        dispatcher
            .When(x => x.DispatchAsync(
                Arg.Any<IReadOnlyList<NotificationRequest>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => dispatchedRecipients.AddRange(
                call.Arg<IReadOnlyList<NotificationRequest>>().Select(x => x.RecipientUserId)));

        var handler = new IssuePublishedHandler(
            subs, dispatcher, ecs, auth,
            NullLogger<IssuePublishedHandler>.Instance);

        await handler.Handle(
            new IssuePublished(
                IssueId: Guid.NewGuid(),
                ProjectId: Guid.NewGuid(),
                Title: "Test issue",
                AuthorId: authorId,
                CourseIds: [courseId],
                NotifySubscribers: true),
            default);

        // 650 / 25 = 26 chunks across two bounded subscriber pages.
        await dispatcher.Received(26).DispatchAsync(
            Arg.Any<IReadOnlyList<NotificationRequest>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(recipients, dispatchedRecipients.Order());
        Assert.Equal(650, dispatchedRecipients.Distinct().Count());
        await subs.Received(1).ByEntityPageAsync(
            Arg.Any<string>(), courseId, null, 500, Arg.Any<CancellationToken>());
        await subs.Received(1).ByEntityPageAsync(
            Arg.Any<string>(), courseId, firstPageCursor, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MaterialPublished_With451Subscribers_RoundsUpTo19Chunks()
    {
        ISubscribersQuery subs = Substitute.For<ISubscribersQuery>();
        INotificationDispatcher dispatcher = Substitute.For<INotificationDispatcher>();
        IEducationContentServiceClient ecs = Substitute.For<IEducationContentServiceClient>();
        IAuthServiceClient auth = Substitute.For<IAuthServiceClient>();

        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        IReadOnlyList<Guid> recipients = Enumerable.Range(0, 451)
            .Select(_ => Guid.NewGuid())
            .ToList();

        subs.ByEntityPageAsync(
                Arg.Any<string>(),
                courseId,
                null,
                500,
                Arg.Any<CancellationToken>())
            .Returns(new SubscriberPage(recipients, null));

        ecs.GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                Id: courseId, Slug: "course", Title: "Course",
                Description: "",
                Status: PublicationStatus.PUBLISHED,
                UpdatedAt: DateTime.UtcNow,
                RequiredAccessTags: [],
                AuthorId: authorId)));

        auth.GetAuthorSpaceByAuthorIdAsync(authorId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<AuthorSpaceRouteResponse, Error>(
                new AuthorSpaceRouteResponse(authorId, "author")));

        var handler = new MaterialPublishedHandler(
            subs, dispatcher, ecs, auth,
            NullLogger<MaterialPublishedHandler>.Instance);

        await handler.Handle(
            new MaterialPublished(
                MaterialId: Guid.NewGuid(),
                Title: "Test material",
                AuthorId: authorId,
                CourseIds: [courseId],
                NotifySubscribers: true),
            default);

        // Ceiling check: 451 / 25 = 19 chunks (18 × 25 + 1 partial).
        await dispatcher.Received(19).DispatchAsync(
            Arg.Any<IReadOnlyList<NotificationRequest>>(),
            Arg.Any<CancellationToken>());
    }
}
