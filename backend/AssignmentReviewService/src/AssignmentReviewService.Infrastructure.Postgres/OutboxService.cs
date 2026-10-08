using AssignmentReviewService.Core.Database;
using Wolverine.EntityFrameworkCore;

namespace AssignmentReviewService.Infrastructure.Postgres;

/// <summary>
///     <see cref="IOutboxService"/>-имплементация через Wolverine <c>IDbContextOutbox</c>.
/// </summary>
public sealed class OutboxService : IOutboxService
{
    private readonly IDbContextOutbox<AssignmentReviewServiceDbContext> _outbox;

    public OutboxService(IDbContextOutbox<AssignmentReviewServiceDbContext> outbox) => _outbox = outbox;

    public Task FlushAsync() => _outbox.FlushOutgoingMessagesAsync();

    public async Task PublishAsync<T>(T message)
        where T : class => await _outbox.PublishAsync(message);
}
