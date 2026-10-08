using AuthService.Core.Database;
using Core.Database;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestFixture))]
public class OutboxInfrastructureTests : IntegrationTestsBase
{
    public OutboxInfrastructureTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task PublishWithoutSave_ShouldNotReachCollector()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        await outbox.PublishAsync(new TestMessage("pending"));

        Assert.Empty(OutboxCollector.Messages);
    }

    [Fact]
    public async Task AutoTransactionSave_ShouldFlushCollector()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        ITransactionManager transaction = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var message = new TestMessage("auto");

        await outbox.PublishAsync(message);
        await transaction.SaveChangesAsync();

        Assert.Same(message, Assert.Single(OutboxCollector.OfType<TestMessage>()));
    }

    [Fact]
    public async Task ManualTransaction_ShouldFlushOnlyAfterCommit()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        ITransactionManager transaction = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var message = new TestMessage("manual");

        await transaction.BeginTransactionAsync();
        await outbox.PublishAsync(message);
        await transaction.SaveChangesAsync();
        Assert.Empty(OutboxCollector.Messages);

        await transaction.CommitTransactionAsync();

        Assert.Same(message, Assert.Single(OutboxCollector.OfType<TestMessage>()));
    }

    private sealed record TestMessage(string Value);
}
