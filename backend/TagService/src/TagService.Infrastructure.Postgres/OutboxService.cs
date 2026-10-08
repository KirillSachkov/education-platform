using TagService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace TagService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<TagDbContext> _outbox;

    public OutboxService(IDbContextOutbox<TagDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
