using AuthService.Core.Database;
using AuthService.Core.Features.Users.EventHandlers;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;
using SharedKernel.Exceptions;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class AvatarEventHandlerTests : IntegrationTestsBase
{
    public AvatarEventHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task AvatarBound_ShouldUpdateProfileAndPublishEvent()
    {
        Guid userId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AvatarAssetBoundHandler handler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetBoundHandler>.Instance);

        await handler.HandleAsync(Bound(assetId, userId), CancellationToken.None);

        Guid? avatarId = await ExecuteInDb(async db =>
            (await db.UserProfiles.FindAsync(userId))!.AvatarId);
        Assert.Equal(assetId, avatarId);

        UserAvatarUpdated message = Assert.Single(OutboxCollector.OfType<UserAvatarUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Equal(assetId, message.AvatarId);
    }

    [Fact]
    public async Task AvatarDeleted_ShouldClearProfileAndPublishEvent()
    {
        Guid userId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);
        await ExecuteInDb(async db =>
        {
            UserProfile profile = (await db.UserProfiles.FindAsync(userId))!;
            profile.UpdateAvatar(assetId, DateTime.UtcNow, bindingRevision: 0);
            await db.SaveChangesAsync();
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AvatarAssetDeletedHandler handler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetDeletedHandler>.Instance);

        await handler.HandleAsync(Deleted(assetId, userId), CancellationToken.None);

        Guid? avatarId = await ExecuteInDb(async db =>
            (await db.UserProfiles.FindAsync(userId))!.AvatarId);
        Assert.Null(avatarId);

        UserAvatarUpdated message = Assert.Single(OutboxCollector.OfType<UserAvatarUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Null(message.AvatarId);
    }

    [Fact]
    public async Task AvatarBound_OutboxSaveFailure_ShouldThrowForWolverineRetry()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ITransactionManager actual = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        ITransactionManager failing = Substitute.For<ITransactionManager>();
        failing.GetDbConnection().Returns(actual.GetDbConnection());
        failing.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        AvatarAssetBoundHandler handler = new(
            failing,
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetBoundHandler>.Instance);

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.HandleAsync(Bound(Guid.NewGuid(), userId), CancellationToken.None));
        Assert.Empty(OutboxCollector.Messages);
    }

    [Fact]
    public async Task AvatarBound_StaleRevisionCannotOverwriteNewerAvatar()
    {
        Guid userId = Guid.NewGuid();
        Guid currentAssetId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AvatarAssetBoundHandler handler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetBoundHandler>.Instance);

        await handler.HandleAsync(Bound(currentAssetId, userId, revision: 20), CancellationToken.None);
        OutboxCollector.Clear();
        Guid staleAssetId = Guid.NewGuid();
        await handler.HandleAsync(Bound(staleAssetId, userId, revision: 19), CancellationToken.None);

        UserProfile profile = await ExecuteInDb(async db => (await db.UserProfiles.FindAsync(userId))!);
        Assert.Equal(currentAssetId, profile.AvatarId);
        Assert.Equal(20, profile.AvatarBindingRevision);
        FileAssetDetached detached = Assert.Single(OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(staleAssetId, detached.AssetId);
        Assert.Equal(19, detached.ExpectedBindingRevision);
    }

    [Fact]
    public async Task AvatarBound_ReplacementPublishesRevisionMatchedDetach()
    {
        Guid userId = Guid.NewGuid();
        Guid oldAssetId = Guid.NewGuid();
        Guid newAssetId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AvatarAssetBoundHandler handler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetBoundHandler>.Instance);
        await handler.HandleAsync(Bound(oldAssetId, userId, revision: 30), CancellationToken.None);
        OutboxCollector.Clear();

        await handler.HandleAsync(Bound(newAssetId, userId, revision: 31), CancellationToken.None);

        FileAssetDetached detached = Assert.Single(OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(oldAssetId, detached.AssetId);
        Assert.Equal(30, detached.ExpectedBindingRevision);
    }

    [Fact]
    public async Task AvatarDeleted_PreservesRevisionTombstoneAndLateBoundCannotResurrect()
    {
        Guid userId = Guid.NewGuid();
        Guid deletedAssetId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AvatarAssetBoundHandler boundHandler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetBoundHandler>.Instance);
        AvatarAssetDeletedHandler deletedHandler = new(
            scope.ServiceProvider.GetRequiredService<ITransactionManager>(),
            scope.ServiceProvider.GetRequiredService<IProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            NullLogger<AvatarAssetDeletedHandler>.Instance);

        await boundHandler.HandleAsync(Bound(deletedAssetId, userId, revision: 10), CancellationToken.None);
        await deletedHandler.HandleAsync(Deleted(deletedAssetId, userId, revision: 10), CancellationToken.None);
        OutboxCollector.Clear();

        Guid staleAssetId = Guid.NewGuid();
        await boundHandler.HandleAsync(Bound(staleAssetId, userId, revision: 9), CancellationToken.None);
        await boundHandler.HandleAsync(Bound(deletedAssetId, userId, revision: 10), CancellationToken.None);

        UserProfile profile = await ExecuteInDb(async db => (await db.UserProfiles.FindAsync(userId))!);
        Assert.Null(profile.AvatarId);
        Assert.Equal(10, profile.AvatarBindingRevision);
        FileAssetDetached[] detached = OutboxCollector.OfType<FileAssetDetached>().ToArray();
        Assert.Contains(detached, message =>
            message.AssetId == staleAssetId && message.ExpectedBindingRevision == 9);
        Assert.Contains(detached, message =>
            message.AssetId == deletedAssetId && message.ExpectedBindingRevision == 10);
    }

    private static FileAssetBound Bound(Guid assetId, Guid userId, long revision = 1) =>
        new(
            assetId,
            "image",
            FileEventsRouting.UsageTypes.AVATAR,
            userId,
            FileEventsRouting.EntityTypes.USER,
            revision);

    private static FileAssetDeleted Deleted(Guid assetId, Guid userId, long revision = 0) =>
        new(
            assetId,
            "image",
            FileEventsRouting.UsageTypes.AVATAR,
            userId,
            FileEventsRouting.EntityTypes.USER,
            revision);
}
