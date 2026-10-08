using TelegramBotService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace TelegramBotService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<TelegramBotDbContext> _outbox;

    public OutboxService(IDbContextOutbox<TelegramBotDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
