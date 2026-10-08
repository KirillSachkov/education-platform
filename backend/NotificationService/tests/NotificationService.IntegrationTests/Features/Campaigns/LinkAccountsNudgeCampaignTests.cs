using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Campaigns;

/// <summary>
///     Кампания «привяжите GitHub и Telegram» (#704, epic #696) — три admin-endpoint'а
///     (<c>/notifications/admin/campaigns/link-accounts-nudge/{run,test,recipient-count}</c>)
///     поверх <c>LinkAccountsNudgeCampaignRunner</c>. Аудитория — пользователи БЕЗ
///     GitHub-привязки (<c>githubLinked: false</c>), канал ТОЛЬКО InApp, opt-out'ы работают
///     штатно (в отличие от форсированного <c>EmailLoginNotice</c>). Проверяем:
///     <list type="bullet">
///         <item>run → каждый пользователь со всех страниц получает nudge (type 26),
///         только InApp, deep-link /settings/integrations</item>
///         <item>per-type opt-out уважается — nudge НЕ критичный</item>
///         <item>идемпотентность: повторный run не плодит дубликаты</item>
///         <item>test → nudge приходит ТОЛЬКО вызывающему админу</item>
///         <item>recipient-count → сумма страниц без-GitHub-аудитории</item>
///         <item>auth: не-админ → 403 на каждом endpoint'е</item>
///     </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class LinkAccountsNudgeCampaignTests : NotificationServiceTestsBase
{
    private const string RunPath = "/notifications/admin/campaigns/link-accounts-nudge/run/";
    private const string TestPath = "/notifications/admin/campaigns/link-accounts-nudge/test/";
    private const string RecipientCountPath = "/notifications/admin/campaigns/link-accounts-nudge/recipient-count/";

    public LinkAccountsNudgeCampaignTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Run_UsersWithoutGithubAcrossPages_EachGetsInAppOnlyNudge()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();

        // Две keyset-страницы аудитории БЕЗ GitHub-привязки: [A, B] → NextAfterId=B, [C] → конец.
        StubNotLinkedUserPages(([userA, userB], userB), ([userC], null));

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();

        Assert.Equal(3, run.Queued);

        List<Notification> nudges = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.LinkAccountsNudge)
            .ToListAsync());

        Assert.Equal(3, nudges.Count);
        Assert.Equal(
            new[] { userA, userB, userC }.Order().ToArray(),
            nudges.Select(n => n.RecipientUserId).Order().ToArray());

        foreach (Notification nudge in nudges)
        {
            // Канал только InApp — ни Email, ни Telegram у шаблона нет.
            Assert.Equal(NotificationChannel.InApp, nudge.Channels);
            // Deep-link на настройки интеграций запечён в payload.targetUrl.
            Assert.Contains("/settings/integrations", nudge.Payload, StringComparison.Ordinal);
        }

        // Аудитория запрошена именно с githubLinked=false (юзеры БЕЗ привязки).
        await AuthServiceClient.Received().GetAllUserIdsAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_UserOptedOutOfType_NudgeSkipped()
    {
        Guid optedOut = Guid.NewGuid();
        Guid regular = Guid.NewGuid();
        StubNotLinkedUserPages(([optedOut, regular], null));

        // Nudge — НЕ критичное уведомление: per-type opt-out работает штатно.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IUserOptOutsRepository repo = scope.ServiceProvider.GetRequiredService<IUserOptOutsRepository>();
            await repo.ReplaceAsync(optedOut, [NotificationType.LinkAccountsNudge]);
        }

        AuthenticateAsAdmin();
        await PostRunAsync();

        List<Guid> recipients = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.LinkAccountsNudge)
            .Select(n => n.RecipientUserId)
            .ToListAsync());

        Assert.Equal([regular], recipients);
    }

    [Fact]
    public async Task Run_SecondTimeImmediately_NoDuplicateNudges()
    {
        Guid userA = Guid.NewGuid();
        StubNotLinkedUserPages(([userA], null));

        AuthenticateAsAdmin();

        RunCampaignResponse first = await PostRunAsync();
        Assert.Equal(1, first.Queued);

        // Повторный проход сразу: A уже уведомлён под фиксированным campaign-correlation →
        // unique-индекс гасит дубликат.
        RunCampaignResponse second = await PostRunAsync();
        Assert.Equal(1, second.Queued);

        int nudgeCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LinkAccountsNudge && n.RecipientUserId == userA));
        Assert.Equal(1, nudgeCount);
    }

    [Fact]
    public async Task Test_DispatchesNudgeOnlyToCallingAdmin()
    {
        Guid admin = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();

        AuthenticateAsAdmin(admin);
        SendTestCampaignResponse resp = await PostTestAsync();

        Assert.True(resp.Sent);

        int adminNudges = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LinkAccountsNudge && n.RecipientUserId == admin));
        Assert.Equal(1, adminNudges);

        int otherNudges = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LinkAccountsNudge && n.RecipientUserId == otherUser));
        Assert.Equal(0, otherNudges);

        int totalNudges = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.LinkAccountsNudge));
        Assert.Equal(1, totalNudges);
    }

    [Fact]
    public async Task RecipientCount_ReturnsSumOfNotLinkedPages()
    {
        Guid u1 = Guid.NewGuid();
        Guid u2 = Guid.NewGuid();
        Guid u3 = Guid.NewGuid();

        StubNotLinkedUserPages(([u1, u2], u2), ([u3], null));

        AuthenticateAsAdmin();
        CampaignRecipientCountResponse resp = await GetRecipientCountAsync();

        Assert.Equal(3, resp.Count);

        await AuthServiceClient.Received().GetAllUserIdsAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == false), Arg.Any<CancellationToken>());
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
    ///     Стабит keyset-страницы аудитории БЕЗ GitHub-привязки: ключ — afterId, с которым
    ///     придёт runner. Любой другой запрос (в т.ч. с неверным githubLinked-флагом) получает
    ///     пустую страницу — кампания с неправильной аудиторией никого не уведомит и тест упадёт.
    /// </summary>
    private void StubNotLinkedUserPages(params (Guid[] UserIds, Guid? NextAfterId)[] pages)
    {
        // Fallback: любые (afterId, githubLinked) → пустая страница.
        AuthServiceClient
            .GetAllUserIdsAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<AllUserIdsResponse, Error>(new AllUserIdsResponse([], null)));

        // Ключ Guid.Empty = первая страница (afterId == null).
        Dictionary<Guid, AllUserIdsResponse> byAfterId = [];
        Guid? expectedAfter = null;
        foreach ((Guid[] userIds, Guid? nextAfterId) in pages)
        {
            byAfterId[expectedAfter ?? Guid.Empty] = new AllUserIdsResponse(userIds, nextAfterId);
            expectedAfter = nextAfterId;
        }

        AuthServiceClient
            .GetAllUserIdsAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == false), Arg.Any<CancellationToken>())
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
