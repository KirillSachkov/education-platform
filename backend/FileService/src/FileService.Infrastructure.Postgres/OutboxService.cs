using FileService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace FileService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<FileServiceDbContext> _outbox;

    public OutboxService(IDbContextOutbox<FileServiceDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}