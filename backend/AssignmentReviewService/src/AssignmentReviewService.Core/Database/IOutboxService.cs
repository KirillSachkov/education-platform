namespace AssignmentReviewService.Core.Database;

/// <summary>
///     Тонкая обёртка над <c>IDbContextOutbox</c>. Use-case handlers работают
///     с этим интерфейсом и не зависят напрямую от Wolverine.
/// </summary>
public interface IOutboxService
{
    Task PublishAsync<T>(T message)
        where T : class;

    Task FlushAsync();
}
