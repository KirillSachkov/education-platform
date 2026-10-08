using AccessService.Core.Features.Integrations.GitHubApp.Services;
using Shared.GitHubApp;

namespace AccessService.IntegrationTests.Features.Integrations.GitHubApp;

public sealed class InMemoryInstallStateStoreTests
{
    [Fact]
    public async Task SetAsync_then_ConsumeAsync_returns_data()
    {
        InMemoryInstallStateStore<InstallStateData> store = new(TimeProvider.System);
        Guid authorId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();

        await store.SetAsync("token-1", new InstallStateData(authorId, planId), TimeSpan.FromMinutes(10));

        InstallStateData? result = await store.ConsumeAsync("token-1");

        Assert.NotNull(result);
        Assert.Equal(authorId, result!.AuthorId);
        Assert.Equal(planId, result.PlanId);
    }

    [Fact]
    public async Task ConsumeAsync_removes_token_after_use()
    {
        InMemoryInstallStateStore<InstallStateData> store = new(TimeProvider.System);
        await store.SetAsync("token-2", new InstallStateData(Guid.NewGuid(), null), TimeSpan.FromMinutes(10));

        InstallStateData? first = await store.ConsumeAsync("token-2");
        InstallStateData? second = await store.ConsumeAsync("token-2");

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_returns_null_for_unknown_token()
    {
        InMemoryInstallStateStore<InstallStateData> store = new(TimeProvider.System);

        InstallStateData? result = await store.ConsumeAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeAsync_returns_null_for_expired_token()
    {
        ManualTimeProvider time = new();
        InMemoryInstallStateStore<InstallStateData> store = new(time);
        await store.SetAsync("token-3", new InstallStateData(Guid.NewGuid(), null), TimeSpan.FromMinutes(10));

        time.Advance(TimeSpan.FromMinutes(11));

        InstallStateData? result = await store.ConsumeAsync("token-3");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_supports_null_planId()
    {
        InMemoryInstallStateStore<InstallStateData> store = new(TimeProvider.System);

        await store.SetAsync("token-4", new InstallStateData(Guid.NewGuid(), null), TimeSpan.FromMinutes(10));

        InstallStateData? result = await store.ConsumeAsync("token-4");
        Assert.NotNull(result);
        Assert.Null(result!.PlanId);
    }

    /// <summary>Минимальный TimeProvider stub без зависимостей.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
