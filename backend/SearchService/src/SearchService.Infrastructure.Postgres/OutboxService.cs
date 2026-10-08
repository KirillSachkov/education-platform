using SearchService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace SearchService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<SearchDbContext> _outbox;

    public OutboxService(IDbContextOutbox<SearchDbContext> outbox)
    {
        _outbox = outbox;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        await _outbox.PublishAsync(message);
        await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}
