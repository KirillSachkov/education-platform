using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AuthService.IntegrationTests.Features.Users;

/// <summary>
///     <c>GET /internal/users/ids</c> (#532) — keyset-страницы id всех НЕ залоченных
///     пользователей для платформенного еженедельного дайджеста. Проверяем: роль
///     SERVICE/ADMIN, пагинацию (limit + NextAfterId), исключение залоченных.
///     Плюс фильтр аудитории <c>?githubLinked=true|false</c> (#699, epic #696) —
///     по наличию строки <c>user_logins</c> с <c>login_provider = 'GitHub'</c>.
/// </summary>
[Collection(nameof(IntegrationTestFixture))]
public class GetAllUserIdsTests : IntegrationTestsBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GetAllUserIdsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetAllUserIds_WithoutServiceOrAdminRole_ReturnsForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student-ids@test.com", "platform-student");

        HttpResponseMessage response = await HttpClient.GetAsync("/internal/users/ids?limit=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAllUserIds_AnonymousRequest_ReturnsUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/internal/users/ids?limit=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllUserIds_PagesThroughAllUsers_AscendingWithoutDuplicates()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();
        await SeedUserAsync(userA, "A", "ids-a@test.com", "platform-student");
        await SeedUserAsync(userB, "B", "ids-b@test.com", "platform-student");
        await SeedUserAsync(userC, "C", "ids-c@test.com", "platform-student");

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = [];
        Guid? afterId = null;
        int pages = 0;

        do
        {
            string url = afterId is null
                ? "/internal/users/ids?limit=2"
                : $"/internal/users/ids?afterId={afterId}&limit=2";

            AllUserIdsResponse page = await GetPageAsync(url);

            Assert.True(page.UserIds.Count <= 2, "page must respect limit");
            collected.AddRange(page.UserIds);
            afterId = page.NextAfterId;
            pages++;
        }
        while (afterId is not null && pages < 50);

        // Все посеянные есть, порядок строго возрастающий (keyset), дубликатов нет.
        Assert.Contains(userA, collected);
        Assert.Contains(userB, collected);
        Assert.Contains(userC, collected);
        Assert.Equal(collected.OrderBy(x => x).ToList(), collected);
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    [Fact]
    public async Task GetAllUserIds_LockedUser_Excluded()
    {
        Guid activeUser = Guid.NewGuid();
        Guid lockedUser = Guid.NewGuid();
        await SeedUserAsync(activeUser, "Active", "ids-active@test.com", "platform-student");
        await SeedUserAsync(lockedUser, "Locked", "ids-locked@test.com", "platform-student");

        DateTime lockoutEnd = DateTime.UtcNow.AddDays(1);
        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET lockout_end = {lockoutEnd} WHERE id = {lockedUser}"));

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = [];
        Guid? afterId = null;
        do
        {
            string url = afterId is null
                ? "/internal/users/ids?limit=500"
                : $"/internal/users/ids?afterId={afterId}&limit=500";
            AllUserIdsResponse page = await GetPageAsync(url);
            collected.AddRange(page.UserIds);
            afterId = page.NextAfterId;
        }
        while (afterId is not null);

        Assert.Contains(activeUser, collected);
        Assert.DoesNotContain(lockedUser, collected);
    }

    [Fact]
    public async Task GetAllUserIds_GithubLinkedTrue_ReturnsOnlyGithubLinkedUsers()
    {
        Guid linkedUser = Guid.NewGuid();
        Guid unlinkedUser = Guid.NewGuid();
        Guid telegramOnlyUser = Guid.NewGuid();
        await SeedUserAsync(linkedUser, "Linked", "gh-linked@test.com", "platform-student");
        await SeedUserAsync(unlinkedUser, "Unlinked", "gh-unlinked@test.com", "platform-student");
        await SeedUserAsync(telegramOnlyUser, "TgOnly", "gh-tg-only@test.com", "platform-student");
        await LinkGitHubAsync(linkedUser, "10001");
        await LinkTelegramAsync(telegramOnlyUser, 555_001);

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = await CollectAllPagesAsync("githubLinked=true");

        Assert.Contains(linkedUser, collected);
        Assert.DoesNotContain(unlinkedUser, collected);
        // Telegram-привязка не считается GitHub-привязкой.
        Assert.DoesNotContain(telegramOnlyUser, collected);
    }

    [Fact]
    public async Task GetAllUserIds_GithubLinkedFalse_ReturnsOnlyUsersWithoutGithubLink()
    {
        Guid linkedUser = Guid.NewGuid();
        Guid unlinkedUser = Guid.NewGuid();
        Guid telegramOnlyUser = Guid.NewGuid();
        await SeedUserAsync(linkedUser, "Linked", "ghf-linked@test.com", "platform-student");
        await SeedUserAsync(unlinkedUser, "Unlinked", "ghf-unlinked@test.com", "platform-student");
        await SeedUserAsync(telegramOnlyUser, "TgOnly", "ghf-tg-only@test.com", "platform-student");
        await LinkGitHubAsync(linkedUser, "10002");
        await LinkTelegramAsync(telegramOnlyUser, 555_002);

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = await CollectAllPagesAsync("githubLinked=false");

        Assert.DoesNotContain(linkedUser, collected);
        Assert.Contains(unlinkedUser, collected);
        // Telegram-only юзер — без GitHub-привязки → входит в false-набор.
        Assert.Contains(telegramOnlyUser, collected);
    }

    [Fact]
    public async Task GetAllUserIds_GithubLinkedOmitted_ReturnsBothLinkedAndUnlinked()
    {
        Guid linkedUser = Guid.NewGuid();
        Guid unlinkedUser = Guid.NewGuid();
        await SeedUserAsync(linkedUser, "Linked", "gho-linked@test.com", "platform-student");
        await SeedUserAsync(unlinkedUser, "Unlinked", "gho-unlinked@test.com", "platform-student");
        await LinkGitHubAsync(linkedUser, "10003");

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = await CollectAllPagesAsync(githubFilter: null);

        Assert.Contains(linkedUser, collected);
        Assert.Contains(unlinkedUser, collected);
    }

    [Fact]
    public async Task GetAllUserIds_GithubLinkedTrue_PaginatesWithKeyset()
    {
        Guid linkedA = Guid.NewGuid();
        Guid linkedB = Guid.NewGuid();
        Guid linkedC = Guid.NewGuid();
        Guid unlinkedUser = Guid.NewGuid();
        await SeedUserAsync(linkedA, "LA", "ghp-a@test.com", "platform-student");
        await SeedUserAsync(linkedB, "LB", "ghp-b@test.com", "platform-student");
        await SeedUserAsync(linkedC, "LC", "ghp-c@test.com", "platform-student");
        await SeedUserAsync(unlinkedUser, "U", "ghp-u@test.com", "platform-student");
        await LinkGitHubAsync(linkedA, "10004");
        await LinkGitHubAsync(linkedB, "10005");
        await LinkGitHubAsync(linkedC, "10006");

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        List<Guid> collected = [];
        Guid? afterId = null;
        int pages = 0;

        do
        {
            string url = afterId is null
                ? "/internal/users/ids?limit=2&githubLinked=true"
                : $"/internal/users/ids?afterId={afterId}&limit=2&githubLinked=true";

            AllUserIdsResponse page = await GetPageAsync(url);

            Assert.True(page.UserIds.Count <= 2, "page must respect limit");
            collected.AddRange(page.UserIds);
            afterId = page.NextAfterId;
            pages++;
        }
        while (afterId is not null && pages < 50);

        Assert.Contains(linkedA, collected);
        Assert.Contains(linkedB, collected);
        Assert.Contains(linkedC, collected);
        Assert.DoesNotContain(unlinkedUser, collected);
        // Keyset: строго возрастающий порядок, без дубликатов между страницами.
        Assert.Equal(collected.OrderBy(x => x).ToList(), collected);
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    private async Task<List<Guid>> CollectAllPagesAsync(string? githubFilter)
    {
        List<Guid> collected = [];
        Guid? afterId = null;
        int pages = 0;

        do
        {
            string url = afterId is null
                ? "/internal/users/ids?limit=500"
                : $"/internal/users/ids?afterId={afterId}&limit=500";
            if (githubFilter is not null)
                url += $"&{githubFilter}";

            AllUserIdsResponse page = await GetPageAsync(url);
            collected.AddRange(page.UserIds);
            afterId = page.NextAfterId;
            pages++;
        }
        while (afterId is not null && pages < 50);

        return collected;
    }

    /// <summary>#699 — сидит GitHub-логин в user_logins (login_provider = 'GitHub').</summary>
    private async Task LinkGitHubAsync(Guid userId, string externalId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        IdentityResult result = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(GitHubRoutes.PROVIDER_NAME, externalId, "github-user"));
        Assert.True(result.Succeeded);
    }

    /// <summary>Контроль: Telegram-логин НЕ должен считаться GitHub-привязкой.</summary>
    private async Task LinkTelegramAsync(Guid userId, long telegramUserId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        IdentityResult result = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(
                TelegramProviderConstants.PROVIDER_NAME,
                telegramUserId.ToString(CultureInfo.InvariantCulture),
                "tg-user"));
        Assert.True(result.Succeeded);
    }

    private async Task<AllUserIdsResponse> GetPageAsync(string url)
    {
        HttpResponseMessage response = await HttpClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<AllUserIdsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<AllUserIdsResponse>>(_jsonOptions);
        Assert.NotNull(envelope?.Result);
        return envelope.Result;
    }
}
