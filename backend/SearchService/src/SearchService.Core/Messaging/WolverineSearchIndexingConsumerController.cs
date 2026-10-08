using SearchService.Core.Reindex;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;

namespace SearchService.Core.Messaging;

public sealed class WolverineSearchIndexingConsumerController : ISearchIndexingConsumerController
{
    private readonly IAgentRuntime _agents;
    private readonly ILogger<WolverineSearchIndexingConsumerController> _logger;

    public WolverineSearchIndexingConsumerController(
        IWolverineRuntime runtime,
        ILogger<WolverineSearchIndexingConsumerController> logger)
    {
        _agents = runtime.Agents;
        _logger = logger;
    }

    public async Task<Result<IAsyncDisposable, Error>> PauseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Uri[] listenerUris = RabbitMqConfiguration.SearchIndexingQueueNames
                .Select(static queueName => new Uri($"rabbitmq://queue/{queueName}"))
                .ToArray();

            var restrictions = new AgentRestrictions();

            foreach (Uri listenerUri in listenerUris)
            {
                restrictions.PauseAgent(listenerUri);
            }

            await _agents.ApplyRestrictionsAsync(restrictions, cancellationToken);

            _logger.LogInformation(
                "Paused search indexing consumers: {QueueNames}",
                RabbitMqConfiguration.SearchIndexingQueueNames);

            return new ResumeScope(_agents, listenerUris, _logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause search indexing consumers");

            return Error.Failure(
                "search.indexing_consumers.pause_failed",
                "Не удалось приостановить обработчики индексации поиска")
                .AsTransient();
        }
    }

    private sealed class ResumeScope : IAsyncDisposable
    {
        private readonly IAgentRuntime _agents;
        private readonly IReadOnlyList<Uri> _listenerUris;
        private readonly ILogger _logger;

        public ResumeScope(
            IAgentRuntime agents,
            IReadOnlyList<Uri> listenerUris,
            ILogger logger)
        {
            _agents = agents;
            _listenerUris = listenerUris;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                var restrictions = new AgentRestrictions(
                    _listenerUris
                        .Select(static listenerUri => new AgentRestriction(
                            Guid.CreateVersion7(),
                            listenerUri,
                            AgentRestrictionType.None,
                            0))
                        .ToArray());

                await _agents.ApplyRestrictionsAsync(restrictions, CancellationToken.None);

                _logger.LogInformation(
                    "Resumed search indexing consumers: {QueueNames}",
                    RabbitMqConfiguration.SearchIndexingQueueNames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume search indexing consumers");
                throw;
            }
        }
    }
}
