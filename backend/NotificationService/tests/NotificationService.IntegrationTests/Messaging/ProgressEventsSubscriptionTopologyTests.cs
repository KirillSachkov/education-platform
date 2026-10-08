using Microsoft.Extensions.DependencyInjection;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress;
using Wolverine;
using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;

namespace NotificationService.IntegrationTests.Messaging;

/// <summary>
///     Consumer-сторона контракта <c>progress.events</c> (#1156): очередь
///     <c>notifications.progress.submission_events</c> биндится на те же routing key, которые
///     ProgressService использует в publish-маршрутах (см.
///     <c>ProgressEventsPublishRoutingTests</c>). Handler → уведомление покрыт в
///     <c>IssueAuthorQuestionAskedHandlerTests</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ProgressEventsSubscriptionTopologyTests : NotificationServiceTestsBase
{
    private const string SUBMISSION_EVENTS_QUEUE = "notifications.progress.submission_events";

    public ProgressEventsSubscriptionTopologyTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Theory]
    [InlineData("issue.author_question_asked")]
    [InlineData("issue_submission.author_help_requested")]
    public void SubmissionEventsQueue_IsBoundToProgressEventsKey(string routingKey)
    {
        IWolverineRuntime runtime = Services.GetRequiredService<IWolverineRuntime>();
        RabbitMqTransport transport = runtime.Options.Transports.GetOrCreate<RabbitMqTransport>();

        RabbitMqQueue queue = transport.Queues[SUBMISSION_EVENTS_QUEUE];

        Assert.Contains(queue.Bindings(), b =>
            b.ExchangeName == ProgressEventsRouting.EXCHANGE && b.BindingKey == routingKey);
    }

    [Fact]
    public void AuthorQuestionRoutingKey_MatchesSharedContract() =>
        Assert.Equal("issue.author_question_asked", ProgressEventsRouting.RoutingKeys.IssueAuthorQuestionAsked());
}
