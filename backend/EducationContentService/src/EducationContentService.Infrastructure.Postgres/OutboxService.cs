using EducationContentService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace EducationContentService.Infrastructure.Postgres;

public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<EducationDbContext> _outbox;

    public OutboxService(IDbContextOutbox<EducationDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}