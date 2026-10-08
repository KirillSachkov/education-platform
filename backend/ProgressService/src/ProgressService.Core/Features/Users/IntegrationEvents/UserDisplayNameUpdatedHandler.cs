using AuthService.Contracts.HttpCommunication;
using Core.Database;
using Microsoft.Extensions.Caching.Hybrid;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Users;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace ProgressService.Core.Features.Users.IntegrationEvents;

public sealed class UserDisplayNameUpdatedHandler
{
    private readonly IProgressUserRepository _progressUserRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly ILogger<UserDisplayNameUpdatedHandler> _logger;

    public UserDisplayNameUpdatedHandler(
        IProgressUserRepository progressUserRepository,
        ITransactionManager transactionManager,
        HybridCache cache,
        ILogger<UserDisplayNameUpdatedHandler> logger)
    {
        _progressUserRepository = progressUserRepository;
        _transactionManager = transactionManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task Handle(UserDisplayNameUpdated message, CancellationToken cancellationToken)
    {
        Result<ProgressUser, Error> userResult =
            await _progressUserRepository.GetByAsync(x => x.UserId == message.UserId, cancellationToken);

        if (userResult.IsNotFound())
        {
            _logger.LogWarning("ProgressUser {UserId} not found when updating display name — skipping", message.UserId);
            return;
        }

        if (userResult.IsTransientFailure())
        {
            throw userResult.Error.ToException(); // transient — let Wolverine retry
        }

        if (userResult.IsFailure)
        {
            _logger.LogWarning(
                "Non-retriable error loading ProgressUser {UserId}: {Type}",
                message.UserId, userResult.Error.Type);
            return;
        }

        userResult.Value.UpdateDisplayName(message.DisplayName);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save display name for ProgressUser {UserId}: {Type} — " +
                "rethrowing for Wolverine retry or DLQ",
                message.UserId, saveResult.Error.Type);
            throw saveResult.Error.ToException();
        }

        // Evict cached auth service user data so enrichment fetches fresh values
        string cacheKey = $"{CachedAuthServiceClient.USER_CACHE_KEY_PREFIX}{message.UserId}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }
}
