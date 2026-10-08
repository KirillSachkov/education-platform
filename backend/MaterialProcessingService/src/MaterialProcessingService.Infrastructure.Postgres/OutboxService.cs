using MaterialProcessingService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace MaterialProcessingService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<MaterialProcessingServiceDbContext> _outbox;

    public OutboxService(IDbContextOutbox<MaterialProcessingServiceDbContext> outbox)
    {
        _outbox = outbox;
    }

    public async Task PublishAsync<T>(T message)
        where T : class
    {
        await _outbox.PublishAsync(message);
    }
}
