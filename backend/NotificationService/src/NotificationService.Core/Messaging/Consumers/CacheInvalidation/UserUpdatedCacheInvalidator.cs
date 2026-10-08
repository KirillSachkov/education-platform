using AuthService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace NotificationService.Core.Messaging.Consumers.CacheInvalidation;

/// <summary>
/// Инвалидирует кеш auth user lookup при изменении username / display name.
/// Один handler на оба события — payload разный, но действие то же.
/// </summary>
public sealed class UserUpdatedCacheInvalidator
{
    private readonly HybridCache _cache;

    public UserUpdatedCacheInvalidator(HybridCache cache) => _cache = cache;

    public Task Handle(UserUsernameUpdated evt, CancellationToken ct) =>
        _cache.RemoveAsync(
            $"{CachedAuthServiceClient.USER_CACHE_KEY_PREFIX}{evt.UserId}",
            ct).AsTask();

    public Task Handle(UserDisplayNameUpdated evt, CancellationToken ct) =>
        _cache.RemoveAsync(
            $"{CachedAuthServiceClient.USER_CACHE_KEY_PREFIX}{evt.UserId}",
            ct).AsTask();
}
