using ProgressService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace ProgressService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<ProgressDbContext> _outbox;

    public OutboxService(IDbContextOutbox<ProgressDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}