using AuthService.Contracts.HttpCommunication;
using Core.Database;
using Microsoft.Extensions.Caching.Hybrid;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Users;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace ProgressService.Core.Features.Users.IntegrationEvents;

public sealed class UserUsernameUpdatedHandler
{
    private readonly IProgressUserRepository _progressUserRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly ILogger<UserUsernameUpdatedHandler> _logger;

    public UserUsernameUpdatedHandler(
        IProgressUserRepository progressUserRepository,
        ITransactionManager transactionManager,
        HybridCache cache,
        ILogger<UserUsernameUpdatedHandler> logger)
    {
        _progressUserRepository = progressUserRepository;
        _transactionManager = transactionManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task Handle(UserUsernameUpdated message, CancellationToken cancellationToken)
    {
        Result<ProgressUser, Error> userResult =
            await _progressUserRepository.GetByAsync(x => x.UserId == message.UserId, cancellationToken);

        if (userResult.IsNotFound())
        {
            _logger.LogWarning("ProgressUser {UserId} not found when updating username — skipping", message.UserId);
            return; // permanent — user will never appear in progress schema, do not retry
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

        userResult.Value.UpdateUsername(message.Username);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save username for ProgressUser {UserId}: {Type} — " +
                "rethrowing for Wolverine retry or DLQ",
                message.UserId, saveResult.Error.Type);
            throw saveResult.Error.ToException();
        }

        // Evict cached auth service user data so enrichment fetches fresh values
        string cacheKey = $"{CachedAuthServiceClient.USER_CACHE_KEY_PREFIX}{message.UserId}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }
}
