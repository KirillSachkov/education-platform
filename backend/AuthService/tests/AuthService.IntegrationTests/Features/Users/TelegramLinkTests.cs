using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class TelegramLinkTests : IntegrationTestsBase
{
    public TelegramLinkTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── GET /users/me/telegram/link-token ───────────────────────

    [Fact]
    public async Task GetLinkToken_ReturnsValidTokenAndDeepLinkUrl()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "TgUser", "tg-link@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "TgUser", "tg-link@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/telegram/link-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetTelegramLinkTokenResponse>? body =
            await response.Content.ReadFromJsonAsync<Envelope<GetTelegramLinkTokenResponse>>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Result);

        GetTelegramLinkTokenResponse payload = body.Result!;
        Assert.False(string.IsNullOrWhiteSpace(payload.LinkToken));
        Assert.False(string.IsNullOrWhiteSpace(payload.BotUsername));
        Assert.StartsWith("https://t.me/", payload.DeepLinkUrl, StringComparison.Ordinal);
        Assert.Contains(payload.LinkToken, payload.DeepLinkUrl, StringComparison.Ordinal);
        Assert.Contains(payload.BotUsername, payload.DeepLinkUrl, StringComparison.Ordinal);

        // Token was stored in the token store and maps to the current user.
        FakeTelegramLinkTokenStore store = GetFakeTelegramLinkTokenStore();
        Assert.True(store.Contains(payload.LinkToken));
    }

    [Fact]
    public async Task GetLinkToken_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/telegram/link-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /auth/telegram/verify ──────────────────────────────

    [Fact]
    public async Task VerifyTelegramLink_Service2Service_HappyPath()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "TgVerify", "tg-verify@test.com", PlatformRoles.PARTICIPANT);

        const string token = "test-token-happy-path";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userId);

        // service-to-service: SERVICE role (UserId doesn't need to match a real account)
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        const long telegramUserId = 123456789L;
        const string telegramUsername = "testuser";

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, telegramUsername));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<VerifyTelegramLinkResponse>? body =
            await response.Content.ReadFromJsonAsync<Envelope<VerifyTelegramLinkResponse>>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Result);
        Assert.Equal(userId, body.Result!.UserId);
        Assert.Equal("tg-verify@test.com", body.Result!.Username);

        // AspNetUserLogins contains the Telegram login for this user.
        UserLoginInfo? telegramLogin = await FindUserLoginAsync(userId, TelegramProviderConstants.PROVIDER_NAME);
        Assert.NotNull(telegramLogin);
        Assert.Equal(telegramUserId.ToString(CultureInfo.InvariantCulture), telegramLogin!.ProviderKey);

        // Token is consumed (single-use).
        Assert.False(GetFakeTelegramLinkTokenStore().Contains(token));

        UserTelegramLinked linked = Assert.Single(OutboxCollector.OfType<UserTelegramLinked>());
        Assert.Equal(userId, linked.UserId);
        Assert.Equal(telegramUserId, linked.TelegramUserId);
    }

    [Fact]
    public async Task VerifyTelegramLink_RepeatedWithSameToken_AfterSuccess_IsIdempotent()
    {
        // Regression (#612): the deep-link START button is "sticky" — Telegram re-sends the
        // SAME token on every press, and the bot's HTTP client retries verify 3× on transient
        // errors. Once the token is gone, a repeat must NOT report "expired/already used" when
        // the Telegram account is in fact already linked to the same user — it must be idempotent.
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "TgRepeat", "tg-repeat@test.com", PlatformRoles.PARTICIPANT);

        const string token = "test-token-repeat";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userId);
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        const long telegramUserId = 222333444L;

        HttpResponseMessage first = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, "repeatuser"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same (now-consumed) token arrives again — sticky START / Polly retry.
        HttpResponseMessage second = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, "repeatuser"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Envelope<VerifyTelegramLinkResponse>? body =
            await second.Content.ReadFromJsonAsync<Envelope<VerifyTelegramLinkResponse>>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Result);
        Assert.Equal(userId, body.Result!.UserId);
    }

    [Fact]
    public async Task VerifyTelegramLink_WhenLinkFails_TokenIsNotConsumed()
    {
        // Regression (#612): the token must be burned ONLY after a durable commit. If verify
        // fails (here: the Telegram id is already bound to another user → conflict), the token
        // must survive so a corrected/retried attempt is not locked out by a self-inflicted
        // "expired/already used" error.
        Guid userA = Guid.NewGuid();
        await SeedUserAsync(userA, "OwnerA", "owner-a@test.com", PlatformRoles.PARTICIPANT);
        const long telegramUserId = 9090909L;
        await AddTelegramLoginAsync(userA, telegramUserId);

        Guid userB = Guid.NewGuid();
        await SeedUserAsync(userB, "OwnerB", "owner-b@test.com", PlatformRoles.PARTICIPANT);
        const string token = "token-not-burned-on-failure";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userB);

        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, "userB"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Token must NOT have been consumed — consume happens only after a successful link.
        Assert.True(GetFakeTelegramLinkTokenStore().Contains(token));
    }

    [Fact]
    public async Task VerifyTelegramLink_ExpiredOrUnknownToken_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest("definitely-not-a-real-token", 11111L, "ghost"));

        // Error.Validation → 400 Bad Request (see ErrorResult.GetStatusCodeFromErrorType).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("telegram.link.token_expired", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyTelegramLink_AuthenticatedAsRegularUser_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Regular", "regular@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Regular", "regular@test.com", PlatformRoles.PARTICIPANT);

        const string token = "should-not-matter";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userId);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, 42L, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Token must NOT have been consumed — authorization runs before the handler.
        Assert.True(GetFakeTelegramLinkTokenStore().Contains(token));
    }

    [Fact]
    public async Task VerifyTelegramLink_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest("whatever", 42L, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyTelegramLink_TelegramAccountBoundToAnotherUser_ShouldReturnConflict()
    {
        // User A already has Telegram linked.
        Guid userA = Guid.NewGuid();
        await SeedUserAsync(userA, "UserA", "user-a@test.com", PlatformRoles.PARTICIPANT);
        const long telegramUserId = 7777777L;
        await AddTelegramLoginAsync(userA, telegramUserId);

        // User B requests linking for the SAME telegram id.
        Guid userB = Guid.NewGuid();
        await SeedUserAsync(userB, "UserB", "user-b@test.com", PlatformRoles.PARTICIPANT);

        const string token = "token-for-user-b";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userB);

        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, "other"));

        // Error.Conflict → 409.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("telegram.link.already_linked_to_other", body, StringComparison.Ordinal);
    }

    // ── POST /users/me/telegram/unlink ──────────────────────────

    [Fact]
    public async Task UnlinkTelegram_UserWithTelegramLinked_ShouldRemoveLogin()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "TgUnlink", "tg-unlink@test.com", PlatformRoles.PARTICIPANT);
        await AddTelegramLoginAsync(userId, 555555L);
        AuthorizeAs(userId, "TgUnlink", "tg-unlink@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsync("/users/me/telegram/unlink", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserLoginInfo? telegramLogin = await FindUserLoginAsync(userId, TelegramProviderConstants.PROVIDER_NAME);
        Assert.Null(telegramLogin);

        UserTelegramUnlinked unlinked =
            Assert.Single(OutboxCollector.OfType<UserTelegramUnlinked>());
        Assert.Equal(userId, unlinked.UserId);
        Assert.Equal(555555L, unlinked.TelegramUserId);
    }

    [Fact]
    public async Task UnlinkTelegram_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsync("/users/me/telegram/unlink", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /internal/telegram/unlink-by-telegram-id ───────────

    [Fact]
    public async Task UnlinkByTelegramId_Service2Service_ShouldRemoveLogin()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "TgInternalUnlink", "tg-internal@test.com", PlatformRoles.PARTICIPANT);
        const long telegramUserId = 888888L;
        await AddTelegramLoginAsync(userId, telegramUserId);

        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(telegramUserId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserLoginInfo? telegramLogin = await FindUserLoginAsync(userId, TelegramProviderConstants.PROVIDER_NAME);
        Assert.Null(telegramLogin);

        UserTelegramUnlinked unlinked =
            Assert.Single(OutboxCollector.OfType<UserTelegramUnlinked>());
        Assert.Equal(userId, unlinked.UserId);
        Assert.Equal(telegramUserId, unlinked.TelegramUserId);
    }

    [Fact]
    public async Task UnlinkByTelegramId_NotLinked_ShouldBeIdempotent()
    {
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(99999999L));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkByTelegramId_InvalidTelegramId_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(0L));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkByTelegramId_AuthenticatedAsRegularUser_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Regular", "regular-internal@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Regular", "regular-internal@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(123L));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkByTelegramId_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(123L));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkByTelegramId_FollowedByVerify_AllowsRelinkToDifferentUser()
    {
        // Reproduces the bot-unlink → relink-from-other-account scenario.
        // Step 1: User A links Telegram 555L.
        Guid userA = Guid.NewGuid();
        await SeedUserAsync(userA, "UserA", "user-a-relink@test.com", PlatformRoles.PARTICIPANT);
        const long telegramUserId = 555L;
        await AddTelegramLoginAsync(userA, telegramUserId);

        // Step 2: bot calls internal unlink (full OIDC unlink, not soft).
        AuthorizeAs(Guid.NewGuid(), "bot", "bot@service", PlatformRoles.SERVICE);
        HttpResponseMessage unlinkResponse = await HttpClient.PostAsJsonAsync(
            "/internal/telegram/unlink-by-telegram-id",
            new UnlinkTelegramByTelegramIdRequest(telegramUserId));
        Assert.Equal(HttpStatusCode.OK, unlinkResponse.StatusCode);

        // Step 3: User B can now link the same Telegram id (no orphan row blocking them).
        Guid userB = Guid.NewGuid();
        await SeedUserAsync(userB, "UserB", "user-b-relink@test.com", PlatformRoles.PARTICIPANT);
        const string token = "token-after-bot-unlink";
        GetFakeTelegramLinkTokenStore().SeedToken(token, userB);

        HttpResponseMessage verifyResponse = await HttpClient.PostAsJsonAsync(
            "/auth/telegram/verify",
            new VerifyTelegramLinkRequest(token, telegramUserId, "userB"));

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        UserLoginInfo? newLogin = await FindUserLoginAsync(userB, TelegramProviderConstants.PROVIDER_NAME);
        Assert.NotNull(newLogin);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task AddTelegramLoginAsync(Guid userId, long telegramUserId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);

        IdentityResult result = await userManager.AddLoginAsync(
            user!,
            new UserLoginInfo(
                TelegramProviderConstants.PROVIDER_NAME,
                telegramUserId.ToString(CultureInfo.InvariantCulture),
                "tg-display"));

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed Telegram login: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    private async Task<UserLoginInfo?> FindUserLoginAsync(Guid userId, string provider)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);
        return logins.FirstOrDefault(l => string.Equals(l.LoginProvider, provider, StringComparison.Ordinal));
    }
}
