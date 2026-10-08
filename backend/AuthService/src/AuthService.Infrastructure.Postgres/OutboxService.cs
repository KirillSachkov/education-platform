using AuthService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<AuthDbContext> _outbox;

    public OutboxService(IDbContextOutbox<AuthDbContext> outbox) => _outbox = outbox;

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
