using Core.Database;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Users;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace ProgressService.Core.Features.Users.IntegrationEvents;

public sealed class UserCreatedHandler
{
    private readonly IProgressUserRepository _progressUserRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<UserCreatedHandler> _logger;

    public UserCreatedHandler(
        IProgressUserRepository progressUserRepository,
        ITransactionManager transactionManager,
        ILogger<UserCreatedHandler> logger)
    {
        _progressUserRepository = progressUserRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(UserCreated message, CancellationToken cancellationToken)
    {
        // Idempotency guard — if user already exists, skip without error
        Result<ProgressUser, Error> existingUser =
            await _progressUserRepository.GetByAsync(x => x.UserId == message.UserId, cancellationToken);
        if (existingUser.IsSuccess)
        {
            _logger.LogInformation("ProgressUser {UserId} already exists, skipping creation", message.UserId);
            return;
        }

        if (existingUser.IsTransientFailure())
        {
            // Transient infrastructure failure — let Wolverine retry
            throw existingUser.Error.ToException();
        }

        if (existingUser.IsFailureExceptNotFound())
        {
            // Permanent error (validation/conflict/auth): log and ack, do not retry
            _logger.LogWarning(
                "Non-retriable error checking existing ProgressUser {UserId}: {Type}",
                message.UserId, existingUser.Error.Type);
            return;
        }

        Result<ProgressUser, Error> createResult = ProgressUser.Create(message.UserId, message.Username, message.DisplayName);
        if (createResult.IsFailure)
        {
            _logger.LogWarning("Failed to create ProgressUser for {UserId}: {Error}",
                message.UserId, createResult.Error.Type);
            return; // permanent validation error, do not retry
        }

        await _progressUserRepository.AddAsync(createResult.Value, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save ProgressUser {UserId}: {Type} — rethrowing for Wolverine retry or DLQ",
                message.UserId, saveResult.Error.Type);
            throw saveResult.Error.ToException();
        }
    }
}
