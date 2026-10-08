using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Campaigns;

/// <summary>
///     Кампания «приглашение пройти тест уровня» (#554) — три admin-endpoint'а
///     (<c>/notifications/admin/campaigns/level-test-invite/{run,test,recipient-count}</c>)
///     поверх <c>LevelTestInviteCampaignRunner</c>. Модель та же, что у еженедельного дайджеста
///     (<see cref="Digest.WeeklyDigestTests"/>): all-users dispatch по keyset-страницам id из
///     AuthService. Проверяем:
///     <list type="bullet">
///         <item>run → каждый пользователь со всех keyset-страниц получает invite (type 19)</item>
///         <item>идемпотентность: повторный run не плодит дубликаты (фиксированный campaign GUID)</item>
///         <item>test → приглашение приходит ТОЛЬКО вызывающему админу, остальным — нет</item>
///         <item>recipient-count → число адресатов из fake AuthService</item>
///         <item>auth: не-админ → 403 на каждом endpoint'е</item>
///     </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class LevelTestInviteCampaignTests : NotificationServiceTestsBase
{
    private const string RunPath = "/notifications/admin/campaigns/level-test-invite/run/";
    private const string TestPath = "/notifications/admin/campaigns/level-test-invite/test/";
    private const string RecipientCountPath = "/notifications/admin/campaigns/level-test-invite/recipient-count/";

    public LevelTestInviteCampaignTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Run_AllUsersAcrossPages_EachGetsLevelTestInvite()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();

        // Две keyset-страницы: [A, B] → NextAfterId=B, [C] → конец.
        StubUserPages(([userA, userB], userB), ([userC], null));

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();

        Assert.Equal(3, run.Queued);

        List<Notification> invites = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.LevelTestInvite)
            .ToListAsync());

        Assert.Equal(3, invites.Count);
        Assert.Equal(
            new[] { userA, userB, userC }.Order().ToArray(),
            invites.Select(n => n.RecipientUserId).Order().ToArray());
    }

    [Fact]
    public async Task Run_SecondTimeImmediately_NoDuplicateInvites()
    {
        Guid userA = Guid.NewGuid();
        StubUserPages(([userA], null));

        AuthenticateAsAdmin();

        RunCampaignResponse first = await PostRunAsync();
        Assert.Equal(1, first.Queued);

        // Повторный проход сразу: A уже приглашён под фиксированным campaign-correlation →
        // unique-индекс гасит дубликат (queued по-прежнему считает адресата прохода).
        RunCampaignResponse second = await PostRunAsync();
        Assert.Equal(1, second.Queued);

        int inviteCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LevelTestInvite && n.RecipientUserId == userA));
        Assert.Equal(1, inviteCount);
    }

    [Fact]
    public async Task Test_DispatchesInviteOnlyToCallingAdmin()
    {
        Guid admin = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();

        AuthenticateAsAdmin(admin);
        SendTestCampaignResponse resp = await PostTestAsync();

        Assert.True(resp.Sent);

        int adminInvites = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LevelTestInvite && n.RecipientUserId == admin));
        Assert.Equal(1, adminInvites);

        // Никому, кроме вызывающего админа, тестовая отправка не уходит.
        int otherInvites = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LevelTestInvite && n.RecipientUserId == otherUser));
        Assert.Equal(0, otherInvites);

        int totalInvites = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LevelTestInvite));
        Assert.Equal(1, totalInvites);
    }

    [Fact]
    public async Task RecipientCount_ReturnsSumOfFakeUserPages()
    {
        Guid u1 = Guid.NewGuid();
        Guid u2 = Guid.NewGuid();
        Guid u3 = Guid.NewGuid();

        // Три пользователя на двух keyset-страницах: [u1, u2] → NextAfterId=u2, [u3] → конец.
        // recipient-count суммирует обе страницы → 3.
        StubUserPages(([u1, u2], u2), ([u3], null));

        AuthenticateAsAdmin();
        CampaignRecipientCountResponse resp = await GetRecipientCountAsync();

        Assert.Equal(3, resp.Count);
    }

    [Theory]
    [InlineData(RunPath, "POST")]
    [InlineData(TestPath, "POST")]
    [InlineData(RecipientCountPath, "GET")]
    public async Task Endpoints_NonAdmin_Forbidden(string path, string method)
    {
        AuthenticateAs(Guid.NewGuid()); // обычный user без Platform.ADMIN permission

        HttpResponseMessage resp = string.Equals(method, "POST", StringComparison.Ordinal)
            ? await AppHttpClient.PostAsync(new Uri(path, UriKind.Relative), content: null)
            : await AppHttpClient.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // -- helpers --

    /// <summary>
    ///     Стабит keyset-страницы AuthService: ключ — afterId, с которым придёт runner
    ///     (null для первой страницы, NextAfterId предыдущей — для следующих). Зеркалит
    ///     хелпер из <see cref="Digest.WeeklyDigestTests"/>.
    /// </summary>
    private void StubUserPages(params (Guid[] UserIds, Guid? NextAfterId)[] pages)
    {
        // Ключ Guid.Empty = первая страница (afterId == null).
        Dictionary<Guid, AllUserIdsResponse> byAfterId = [];
        Guid? expectedAfter = null;
        foreach ((Guid[] userIds, Guid? nextAfterId) in pages)
        {
            byAfterId[expectedAfter ?? Guid.Empty] = new AllUserIdsResponse(userIds, nextAfterId);
            expectedAfter = nextAfterId;
        }

        AuthServiceClient
            .GetAllUserIdsAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Guid key = ci.Arg<Guid?>() ?? Guid.Empty;
                AllUserIdsResponse page = byAfterId.TryGetValue(key, out AllUserIdsResponse? found)
                    ? found
                    : new AllUserIdsResponse([], null);
                return Result.Success<AllUserIdsResponse, Error>(page);
            });
    }

    private async Task<RunCampaignResponse> PostRunAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(new Uri(RunPath, UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<RunCampaignResponse>(resp);
    }

    private async Task<SendTestCampaignResponse> PostTestAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(new Uri(TestPath, UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<SendTestCampaignResponse>(resp);
    }

    private async Task<CampaignRecipientCountResponse> GetRecipientCountAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(RecipientCountPath, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<CampaignRecipientCountResponse>(resp);
    }

    private static async Task<T> ReadResultAsync<T>(HttpResponseMessage resp)
    {
        Envelope<T> envelope = (await resp.Content.ReadFromJsonAsync<Envelope<T>>())!;
        Assert.False(envelope.IsError, $"Envelope error: {envelope.Error?.Code}");
        return envelope.Result!;
    }

    private sealed class RunCampaignResponse
    {
        public int Queued { get; init; }
    }

    private sealed class SendTestCampaignResponse
    {
        public bool Sent { get; init; }
    }

    private sealed class CampaignRecipientCountResponse
    {
        public int Count { get; init; }
    }

    private sealed class Envelope<T>
    {
        public T? Result { get; init; }
        public ErrorEnv? Error { get; init; }
        public bool IsError { get; init; }
    }

    private sealed class ErrorEnv
    {
        public string? Code { get; init; }
        public string? Message { get; init; }
    }
}
