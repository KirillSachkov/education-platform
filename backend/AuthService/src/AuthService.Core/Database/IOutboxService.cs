namespace AuthService.Core.Database;

public interface IOutboxService
{
    Task PublishAsync<T>(T message) where T : class;
}
