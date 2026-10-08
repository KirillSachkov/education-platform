using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using Wolverine;

namespace ProgressService.IntegrationTests.Messaging;

/// <summary>
///     Publish-маршруты ProgressService в <c>progress.events</c>. Без
///     <c>PublishMessagesToRabbitMqExchange&lt;T&gt;</c> Wolverine не знает, куда отправить
///     событие из outbox, и NotificationService молча не получает его (#383, #1156).
///     Проверяем реальную Wolverine-конфигурацию хоста: destination-exchange и routing key
///     должны совпадать с binding'ом очереди <c>notifications.progress.submission_events</c>.
/// </summary>
public sealed class ProgressEventsPublishRoutingTests : ProgressServiceTestsBase
{
    private static readonly Uri _progressEventsExchangeUri =
        new($"rabbitmq://exchange/{ProgressEventsRouting.EXCHANGE}");

    public ProgressEventsPublishRoutingTests(IntegrationTestsWebFactory factory) : base(factory) { }

    public static TheoryData<object, string> NotificationBoundEvents => new()
    {
        {
            new IssueAuthorQuestionAsked(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "вопрос", DateTimeOffset.UtcNow),
            ProgressEventsRouting.RoutingKeys.IssueAuthorQuestionAsked()
        },
        {
            new IssueSubmissionAuthorHelpRequested(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                DateTimeOffset.UtcNow),
            ProgressEventsRouting.RoutingKeys.IssueSubmissionAuthorHelpRequested()
        },
    };

    [Theory]
    [MemberData(nameof(NotificationBoundEvents))]
    public void Event_IsRoutedToProgressEventsExchange_WithConsumerRoutingKey(object message, string routingKey)
    {
        IMessageBus bus = Services.CreateScope().ServiceProvider.GetRequiredService<IMessageBus>();

        IReadOnlyList<Envelope> envelopes = bus.PreviewSubscriptions(message);

        Envelope envelope = Assert.Single(envelopes);
        Assert.Equal(_progressEventsExchangeUri, envelope.Destination);
        Assert.Equal(routingKey, envelope.TopicName);
    }

    [Fact]
    public async Task AskAuthorQuestion_PublishedEvent_IsRoutedToNotificationBinding()
    {
        Guid issueId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        EducationContentClient.AddEntityOwnership("issue", issueId, courseId, authorId);
        EducationContentClient.AddIssueCourseBinding(issueId, courseId, "dotnet-fullstack");
        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/issues/{issueId}/ask-author/", new { message = "Что значит идемпотентность?" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueAuthorQuestionAsked published = NoOpOutboxService.Published.OfType<IssueAuthorQuestionAsked>().Single();
        Assert.Equal(authorId, published.AuthorId);

        IMessageBus bus = Services.CreateScope().ServiceProvider.GetRequiredService<IMessageBus>();
        Envelope envelope = Assert.Single(bus.PreviewSubscriptions(published));
        Assert.Equal(_progressEventsExchangeUri, envelope.Destination);
        Assert.Equal(ProgressEventsRouting.RoutingKeys.IssueAuthorQuestionAsked(), envelope.TopicName);
    }
}
