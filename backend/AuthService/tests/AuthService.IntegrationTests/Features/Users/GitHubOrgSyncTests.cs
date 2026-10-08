using AuthService.Core.Database;
using AuthService.Core.Services;
using AuthService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class GitHubOrgSyncTests : IntegrationTestsBase
{
    public GitHubOrgSyncTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task TrySync_EmptyOrganizationList_ShouldPublishEmptySnapshot()
    {
        Guid userId = Guid.NewGuid();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        GitHubOrgSyncService service = CreateService(scope);

        int count = await service.TrySyncAsync(
            userId, "github-user", "access-token", CancellationToken.None);

        Assert.Equal(0, count);
        UserGithubLogin message = Assert.Single(OutboxCollector.OfType<UserGithubLogin>());
        Assert.Equal(userId, message.UserId);
        Assert.Empty(message.GithubOrgs);
    }

    [Fact]
    public async Task SyncFromCache_EmptyOrganizationList_ShouldPublishEmptySnapshot()
    {
        Guid userId = Guid.NewGuid();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        GitHubOrgSyncService service = CreateService(scope);

        IReadOnlyList<string> organizations = await service.SyncFromCacheAsync(
            userId, "github-user", CancellationToken.None);

        Assert.Empty(organizations);
        UserGithubLogin message = Assert.Single(OutboxCollector.OfType<UserGithubLogin>());
        Assert.Equal(userId, message.UserId);
        Assert.Empty(message.GithubOrgs);
    }

    [Fact]
    public async Task TrySync_OutboxCommitFailure_ShouldThrow()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        global::Core.Database.ITransactionManager failing =
            Substitute.For<global::Core.Database.ITransactionManager>();
        failing.BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        failing.CommitTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));
        GitHubOrgSyncService service = CreateService(scope, failing);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TrySyncAsync(
                Guid.NewGuid(), "github-user", "access-token", CancellationToken.None));
    }

    private static GitHubOrgSyncService CreateService(
        AsyncServiceScope scope,
        global::Core.Database.ITransactionManager? transactionManager = null) =>
        new(
            new EmptyGitHubOrgService(),
            scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>(),
            scope.ServiceProvider.GetRequiredService<IOutboxService>(),
            transactionManager ?? scope.ServiceProvider
                .GetRequiredService<global::Core.Database.ITransactionManager>(),
            TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<AuthAudit>>());

    private sealed class EmptyGitHubOrgService : IGitHubOrgService
    {
        public Task<Result<bool, Error>> IsMemberOfOrgAsync(
            string accessToken,
            string orgSlug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<bool, Error>(false));

        public Task<Result<IReadOnlyList<string>, Error>> FetchUserOrgsAsync(
            string accessToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<string>, Error>([]));
    }
}
