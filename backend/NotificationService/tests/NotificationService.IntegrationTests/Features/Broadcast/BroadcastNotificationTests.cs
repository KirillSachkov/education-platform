using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.EntityFrameworkCore;
using NotificationService.Contracts.Broadcast.Requests;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.Subscriptions;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Notifications.Events;

namespace NotificationService.IntegrationTests.Features.Broadcast;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BroadcastNotificationTests : NotificationServiceTestsBase
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public BroadcastNotificationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Broadcast_Endpoint_ReturnsEstimatedRecipients_AndOwnershipHolds()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        // Author authenticates — not admin, so ownership check against ECS will run.
        AuthenticateAs(authorId, "platform-author");

        // Seed 3 subscriptions on the target course.
        List<Guid> recipients = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await ExecuteInDb(async db =>
        {
            foreach (Guid userId in recipients)
            {
                Subscription sub = Subscription.Create(userId, SubscriptionEntityType.COURSE, courseId).Value;
                await db.Subscriptions.AddAsync(sub);
            }
            await db.SaveChangesAsync();
        });

        // Mock ownership — this author owns the course.
        EducationContentClient
            .GetCourseLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseDto, Error>(
                new CourseDto(courseId, authorId, Status: "PUBLISHED", HasFreeContent: false)));

        BroadcastNotificationRequest request = new(
            TargetType: SubscriptionEntityType.COURSE,
            TargetId: courseId,
            Title: "New release",
            Body: "Check out the update",
            Channels: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/notifications/broadcast", request);
        string rawBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {response.StatusCode}: {rawBody}");

        Envelope<BroadcastNotificationResponse>? envelope =
            System.Text.Json.JsonSerializer.Deserialize<Envelope<BroadcastNotificationResponse>>(
                rawBody, JsonOpts);
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotEqual(Guid.Empty, envelope.Result!.BroadcastId);
        Assert.Equal(3, envelope.Result.EstimatedRecipients);
    }

    [Fact]
    public async Task Broadcast_Endpoint_PublishesNotificationBroadcastRequested()
    {
        // L2-регрессия: ловит баг «handler забыл SaveChangesAsync/Flush после _outbox.PublishAsync».
        // Без flush'а — outbox-буфер дропается на dispose DbContext'а, endpoint возвращает 200,
        // но событие никуда не уходит. OutboxCollector видит только publish'и, прошедшие через
        // IOutboxService (Pattern A) — если handler забыл SaveChangesAsync, коллектор пуст и тест падает.
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        Subscription sub = Subscription.Create(Guid.NewGuid(), SubscriptionEntityType.COURSE, courseId).Value;
        await ExecuteInDb(async db =>
        {
            await db.Subscriptions.AddAsync(sub);
            await db.SaveChangesAsync();
        });

        EducationContentClient
            .GetCourseLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseDto, Error>(
                new CourseDto(courseId, authorId, Status: "PUBLISHED", HasFreeContent: false)));

        BroadcastNotificationRequest request = new(
            TargetType: SubscriptionEntityType.COURSE,
            TargetId: courseId,
            Title: "Перенос дедлайна",
            Body: "В пятницу",
            Channels: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/notifications/broadcast", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        NotificationBroadcastRequested published =
            OutboxCollector.OfType<NotificationBroadcastRequested>().Single();
        Assert.Equal(courseId, published.TargetId);
        Assert.Equal(SubscriptionEntityType.COURSE, published.TargetType);
        Assert.Equal("Перенос дедлайна", published.Title);
        Assert.Equal("В пятницу", published.Body);
    }

    [Fact]
    public async Task Broadcast_WithoutOwnership_ReturnsForbidden()
    {
        Guid authorId = Guid.NewGuid();
        Guid otherAuthorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(authorId, "platform-author");

        // Course belongs to someone else.
        EducationContentClient
            .GetCourseLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseDto, Error>(
                new CourseDto(courseId, otherAuthorId, Status: "PUBLISHED", HasFreeContent: false)));

        BroadcastNotificationRequest request = new(
            TargetType: SubscriptionEntityType.COURSE,
            TargetId: courseId,
            Title: "Hijack attempt",
            Body: "bad",
            Channels: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/notifications/broadcast", request);

        // Authorization error from the handler is mapped to 403 by the framework.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BroadcastFanout_CreatesOneNotificationPerSubscriber()
    {
        // Seed 3 subscriptions on a course.
        Guid courseId = Guid.NewGuid();
        Guid broadcastId = Guid.NewGuid();
        List<Guid> recipients = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        await ExecuteInDb(async db =>
        {
            foreach (Guid userId in recipients)
            {
                Subscription sub = Subscription.Create(userId, SubscriptionEntityType.COURSE, courseId).Value;
                await db.Subscriptions.AddAsync(sub);
            }
            await db.SaveChangesAsync();
        });

        // BroadcastFanoutHandler resolves CourseRouteContext через ECS — стаб явно,
        // иначе CachedEducationContentServiceClient + NSubstitute может зависнуть на
        // default(Result<>) в HybridCache factory под нагрузкой full-suite.
        EducationContentClient
            .GetCourseSearchLookupAsync(courseId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<CourseSearchLookupDto, Error>(
                new CourseSearchLookupDto(
                    Id: courseId,
                    Slug: "test-course",
                    Title: "Test Course",
                    Description: "",
                    Status: PublicationStatus.PUBLISHED,
                    UpdatedAt: DateTime.UtcNow,
                    RequiredAccessTags: [],
                    AuthorId: null)));

        // Invoke the integration event directly — this drives BroadcastFanoutHandler
        // through NotificationDispatcher and bypasses RabbitMQ (disabled in tests).
        NotificationBroadcastRequested evt = new(
            BroadcastId: broadcastId,
            RequestedByUserId: Guid.NewGuid(),
            TargetType: SubscriptionEntityType.COURSE,
            TargetId: courseId,
            TemplateId: "author.announcement",
            Title: "News",
            Body: "Body text",
            Channels: 0,
            PayloadJson: "{}",
            RequestedAt: DateTimeOffset.UtcNow);

        await InvokeMessageAndWaitAsync(evt);

        // Every subscriber should have exactly one AuthorAnnouncement notification.
        List<Notification> createdNotifications = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(x => x.Type == NotificationType.AuthorAnnouncement)
            .ToListAsync());

        Assert.Equal(3, createdNotifications.Count);

        HashSet<Guid> recipientsFromDb = [.. createdNotifications.Select(x => x.RecipientUserId)];
        foreach (Guid recipient in recipients)
            Assert.Contains(recipient, recipientsFromDb);

        // Each has a distinct correlation (XOR(broadcastId, recipientId)).
        HashSet<Guid?> correlations = [.. createdNotifications.Select(x => x.CorrelationId)];
        Assert.Equal(3, correlations.Count);

        // Payload preserves broadcast context so frontend can route by (type, payload).
        Assert.All(createdNotifications, n =>
            Assert.Contains(courseId.ToString(), n.Payload, StringComparison.OrdinalIgnoreCase));
    }
}
