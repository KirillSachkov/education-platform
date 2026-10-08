using AccessService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace AccessService.Infrastructure.Postgres;

/// <summary>
/// <see cref="IOutboxService"/>-имплементация через Wolverine DbContextOutbox.
/// </summary>
public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<AccessServiceDbContext> _outbox;

    public OutboxService(IDbContextOutbox<AccessServiceDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
