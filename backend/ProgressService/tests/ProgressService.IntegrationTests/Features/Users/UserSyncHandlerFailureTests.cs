using System.Linq.Expressions;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.Users.IntegrationEvents;
using ProgressService.Domain.Users;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;
using SharedKernel.Exceptions;

namespace ProgressService.IntegrationTests.Features.Users;

public sealed class UserSyncHandlerFailureTests
{
    [Fact]
    public async Task UserCreated_existing_user_is_acknowledged_as_an_authoritatively_proven_duplicate()
    {
        Guid userId = Guid.CreateVersion7();
        ProgressUser existingUser = ProgressUser.Create(userId, "student").Value;
        IProgressUserRepository repository = CreateRepositoryWithResult(existingUser);
        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();

        var handler = new UserCreatedHandler(
            repository,
            transactionManager,
            NullLogger<UserCreatedHandler>.Instance);

        await handler.Handle(new UserCreated(userId, "student"), CancellationToken.None);

        await repository.DidNotReceive().AddAsync(Arg.Any<ProgressUser>(), Arg.Any<CancellationToken>());
        await transactionManager.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserCreated_unique_save_failure_is_rethrown_for_Wolverine_retry_or_dlq()
    {
        Guid userId = Guid.CreateVersion7();
        IProgressUserRepository repository = CreateRepositoryWithResult(
            Error.NotFound("progress.user.not.found", "Пользователь не найден"));
        ITransactionManager transactionManager = CreateFailingTransactionManager(
            GeneralErrors.UniqueConstraintViolation("Пользователь"));

        var handler = new UserCreatedHandler(
            repository,
            transactionManager,
            NullLogger<UserCreatedHandler>.Instance);

        PermanentException exception = await Assert.ThrowsAsync<PermanentException>(() => handler.Handle(
            new UserCreated(userId, "student"),
            CancellationToken.None));
        Assert.Equal(ErrorType.CONFLICT, exception.Error.Type);
    }

    [Fact]
    public async Task UserDisplayNameUpdated_foreign_key_save_failure_is_rethrown_for_message_retry_or_dlq()
    {
        Guid userId = Guid.CreateVersion7();
        ProgressUser user = ProgressUser.Create(userId, "student").Value;
        IProgressUserRepository repository = CreateRepositoryWithResult(user);
        ITransactionManager transactionManager = CreateFailingTransactionManager(
            GeneralErrors.ForeignKeyViolation("Пользователь"));

        var handler = new UserDisplayNameUpdatedHandler(
            repository,
            transactionManager,
            Substitute.For<HybridCache>(),
            NullLogger<UserDisplayNameUpdatedHandler>.Instance);

        PermanentException exception = await Assert.ThrowsAsync<PermanentException>(() => handler.Handle(
            new UserDisplayNameUpdated(userId, "Новое имя"),
            CancellationToken.None));
        Assert.Equal(ErrorType.CONFLICT, exception.Error.Type);
    }

    [Fact]
    public async Task UserUsernameUpdated_unique_save_failure_is_rethrown_for_message_retry_or_dlq()
    {
        Guid userId = Guid.CreateVersion7();
        ProgressUser user = ProgressUser.Create(userId, "old-name").Value;
        IProgressUserRepository repository = CreateRepositoryWithResult(user);
        ITransactionManager transactionManager = CreateFailingTransactionManager(
            GeneralErrors.UniqueConstraintViolation("Имя пользователя"));

        var handler = new UserUsernameUpdatedHandler(
            repository,
            transactionManager,
            Substitute.For<HybridCache>(),
            NullLogger<UserUsernameUpdatedHandler>.Instance);

        PermanentException exception = await Assert.ThrowsAsync<PermanentException>(() => handler.Handle(
            new UserUsernameUpdated(userId, "new-name"),
            CancellationToken.None));
        Assert.Equal(ErrorType.CONFLICT, exception.Error.Type);
    }

    private static IProgressUserRepository CreateRepositoryWithResult(Result<ProgressUser, Error> result)
    {
        IProgressUserRepository repository = Substitute.For<IProgressUserRepository>();
        repository.GetByAsync(
                Arg.Any<Expression<Func<ProgressUser, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
        return repository;
    }

    private static ITransactionManager CreateFailingTransactionManager(Error error)
    {
        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(error));
        return transactionManager;
    }
}
