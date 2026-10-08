using AuthService.Core.Database;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Features.Auth;

[Collection(nameof(IntegrationTestFixture))]
public sealed class UserGithubOrgRepositoryTests : IntegrationTestsBase
{
    public UserGithubOrgRepositoryTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task ReplaceAllAsync_InsertsOrgsForNewUser()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Org User", "orgtest@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        // Act
        await repo.ReplaceAllAsync(userId, ["alpha-org", "beta-org"], DateTime.UtcNow, default);

        // Assert
        IReadOnlyList<string> stored = await repo.GetByUserAsync(userId, default);
        Assert.Equal(2, stored.Count);
        Assert.Contains("alpha-org", stored);
        Assert.Contains("beta-org", stored);
    }

    [Fact]
    public async Task ReplaceAllAsync_NormalizesAndDeduplicates()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Norm User", "norm@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        await repo.ReplaceAllAsync(userId, ["My-Org", "MY-ORG", "my-org", "  "], DateTime.UtcNow, default);

        IReadOnlyList<string> stored = await repo.GetByUserAsync(userId, default);
        Assert.Single(stored);
        Assert.Equal("my-org", stored[0]);
    }

    [Fact]
    public async Task ReplaceAllAsync_RemovesOrgsNotInNewSet()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Replace User", "replace@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        await repo.ReplaceAllAsync(userId, ["org-a", "org-b", "org-c"], DateTime.UtcNow, default);

        // Юзер вышел из b и c, остался в a, добавился d.
        await repo.ReplaceAllAsync(userId, ["org-a", "org-d"], DateTime.UtcNow, default);

        IReadOnlyList<string> stored = await repo.GetByUserAsync(userId, default);
        Assert.Equal(2, stored.Count);
        Assert.Contains("org-a", stored);
        Assert.Contains("org-d", stored);
    }

    [Fact]
    public async Task ReplaceAllAsync_EmptySet_RemovesAll()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Empty User", "empty@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        await repo.ReplaceAllAsync(userId, ["org-x", "org-y"], DateTime.UtcNow, default);
        await repo.ReplaceAllAsync(userId, [], DateTime.UtcNow, default);

        IReadOnlyList<string> stored = await repo.GetByUserAsync(userId, default);
        Assert.Empty(stored);
    }

    [Fact]
    public async Task GetUserIdsByOrgAsync_ReturnsOnlyMembersOfOrg()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();
        await SeedUserAsync(userA, "User A", "a@x.com", "platform-participant");
        await SeedUserAsync(userB, "User B", "b@x.com", "platform-participant");
        await SeedUserAsync(userC, "User C", "c@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        await repo.ReplaceAllAsync(userA, ["target-org", "other"], DateTime.UtcNow, default);
        await repo.ReplaceAllAsync(userB, ["target-org"], DateTime.UtcNow, default);
        await repo.ReplaceAllAsync(userC, ["other"], DateTime.UtcNow, default);

        IReadOnlyList<Guid> members = await repo.GetUserIdsByOrgAsync("target-org", default);

        Assert.Equal(2, members.Count);
        Assert.Contains(userA, members);
        Assert.Contains(userB, members);
        Assert.DoesNotContain(userC, members);
    }

    [Fact]
    public async Task GetUserIdsByOrgAsync_LookupIsCaseInsensitive()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Case User", "case@x.com", "platform-participant");

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();

        await repo.ReplaceAllAsync(userId, ["mixed-Case-Org"], DateTime.UtcNow, default);

        IReadOnlyList<Guid> members = await repo.GetUserIdsByOrgAsync("MIXED-case-org", default);
        Assert.Single(members);
        Assert.Equal(userId, members[0]);
    }
}
