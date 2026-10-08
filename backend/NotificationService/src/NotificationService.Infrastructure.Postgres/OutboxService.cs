using NotificationService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace NotificationService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<NotificationDbContext> _outbox;

    public OutboxService(IDbContextOutbox<NotificationDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
