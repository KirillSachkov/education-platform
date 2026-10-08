using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SearchService.Core.Messaging;
using CSharpFunctionalExtensions;
using SharedKernel;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Agents;

namespace SearchService.IntegrationTests.Infrastructure;

public sealed class WolverineSearchIndexingConsumerControllerTests
{
    [Fact]
    public async Task Pause_failure_should_be_transient_so_reindex_message_is_retried()
    {
        IWolverineRuntime runtime = Substitute.For<IWolverineRuntime>();
        IAgentRuntime agents = Substitute.For<IAgentRuntime>();
        runtime.Agents.Returns(agents);
        agents
            .ApplyRestrictionsAsync(Arg.Any<AgentRestrictions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("agent grid unavailable")));

        var controller = new WolverineSearchIndexingConsumerController(
            runtime,
            NullLogger<WolverineSearchIndexingConsumerController>.Instance);

        Result<IAsyncDisposable, Error> result = await controller.PauseAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorBehavior.Transient, result.Error.Behavior);
    }

    [Fact]
    public async Task Resume_failure_should_escape_dispose_so_reindex_message_is_retried()
    {
        IWolverineRuntime runtime = Substitute.For<IWolverineRuntime>();
        IAgentRuntime agents = Substitute.For<IAgentRuntime>();
        runtime.Agents.Returns(agents);

        int applyCalls = 0;
        agents
            .ApplyRestrictionsAsync(Arg.Any<AgentRestrictions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                applyCalls++;
                return applyCalls == 1
                    ? Task.CompletedTask
                    : Task.FromException(new InvalidOperationException("resume failed"));
            });

        var controller = new WolverineSearchIndexingConsumerController(
            runtime,
            NullLogger<WolverineSearchIndexingConsumerController>.Instance);

        Result<IAsyncDisposable, Error> pauseResult = await controller.PauseAsync();

        Assert.True(pauseResult.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pauseResult.Value.DisposeAsync().AsTask());
        Assert.Equal(2, applyCalls);
    }

    [Fact]
    public async Task Resume_should_explicitly_restart_both_search_indexing_consumers()
    {
        IWolverineRuntime runtime = Substitute.For<IWolverineRuntime>();
        IAgentRuntime agents = Substitute.For<IAgentRuntime>();
        runtime.Agents.Returns(agents);

        var appliedRestrictions = new List<AgentRestrictions>();
        agents
            .ApplyRestrictionsAsync(Arg.Any<AgentRestrictions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                appliedRestrictions.Add(callInfo.Arg<AgentRestrictions>());
                return Task.CompletedTask;
            });

        var controller = new WolverineSearchIndexingConsumerController(
            runtime,
            NullLogger<WolverineSearchIndexingConsumerController>.Instance);

        Result<IAsyncDisposable, Error> pauseResult = await controller.PauseAsync();
        await pauseResult.Value.DisposeAsync();

        AgentRestriction[] resumeRestrictions = appliedRestrictions[1].Current.ToArray();
        Assert.Equal(2, resumeRestrictions.Length);
        Assert.All(
            resumeRestrictions,
            restriction => Assert.Equal(AgentRestrictionType.None, restriction.Type));
        Assert.Contains(
            resumeRestrictions,
            restriction => restriction.AgentUri == new Uri("rabbitmq://queue/search.education.lifecycle_events"));
        Assert.Contains(
            resumeRestrictions,
            restriction => restriction.AgentUri == new Uri("rabbitmq://queue/search.tag.events"));
    }
}
