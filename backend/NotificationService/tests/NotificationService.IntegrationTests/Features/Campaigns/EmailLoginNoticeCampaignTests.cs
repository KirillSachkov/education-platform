using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Email;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Campaigns;

/// <summary>Кампания входа по почте: доставка, идемпотентность и доступ администратора.</summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class EmailLoginNoticeCampaignTests : NotificationServiceTestsBase
{
    private const string RUN_PATH = "/notifications/admin/campaigns/email-login-notice/run/";
    private const string TEST_PATH = "/notifications/admin/campaigns/email-login-notice/test/";
    private const string RECIPIENT_COUNT_PATH = "/notifications/admin/campaigns/email-login-notice/recipient-count/";

    public EmailLoginNoticeCampaignTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Run_GithubLinkedUsersAcrossPages_EachGetsNoticeWithTheirEmail()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();

        // Две keyset-страницы githubLinked-аудитории: [A, B] → NextAfterId=B, [C] → конец.
        StubGithubLinkedUserPages(([userA, userB], userB), ([userC], null));
        StubEmailsForAllUsers();

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();

        Assert.Equal(3, run.Queued);

        List<Notification> notices = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.EmailLoginNotice)
            .ToListAsync());

        Assert.Equal(3, notices.Count);
        Assert.Equal(
            new[] { userA, userB, userC }.Order().ToArray(),
            notices.Select(n => n.RecipientUserId).Order().ToArray());

        // {email} подставлен per-user в InApp-тело («Входите по коду на почту {email}»).
        foreach (Notification notice in notices)
        {
            Assert.Contains(EmailOf(notice.RecipientUserId), notice.Body, StringComparison.Ordinal);
            Assert.NotEqual(NotificationChannel.None, notice.Channels & NotificationChannel.Email);
            Assert.NotEqual(NotificationChannel.None, notice.Channels & NotificationChannel.InApp);
        }

        // Аудитория запрошена именно с githubLinked=true (юзеры С привязкой).
        await AuthServiceClient.Received().GetAllUserIdsAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_EmailChannelDisabledByUser_EmailStillForcedAndTransactional()
    {
        Guid userId = Guid.NewGuid();
        StubGithubLinkedUserPages(([userId], null));
        StubEmailsForAllUsers();

        // Юзер явно выключил Email-канал — обычную рассылку это глушит.
        await ExecuteInDb(async db =>
        {
            db.UserNotificationChannels.Add(
                UserNotificationChannels.Create(userId, telegramEnabled: true, emailEnabled: false).Value);
            await db.SaveChangesAsync();
        });

        List<(string To, bool Transactional)> sent = [];
        EmailSender
            .SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                sent.Add((ci.ArgAt<string>(0), ci.ArgAt<bool>(5)));
                return EmailSendResult.Success();
            });

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();
        Assert.Equal(1, run.Queued);

        // Email-бит форсирован шаблоном поверх выключенного канала (критичное уведомление
        // об аккаунте, #704) …
        Notification notice = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(n => n.Type == NotificationType.EmailLoginNotice && n.RecipientUserId == userId));
        Assert.NotEqual(NotificationChannel.None, notice.Channels & NotificationChannel.Email);

        // … и письмо реально ушло, причём transactional=true (provider-level bypass отписки).
        (string to, bool transactional) = Assert.Single(sent);
        Assert.Equal(EmailOf(userId), to);
        Assert.True(transactional);
    }

    [Fact]
    public async Task Run_UserOptedOutOfType_CriticalNoticeStillDelivered()
    {
        Guid userId = Guid.NewGuid();
        StubGithubLinkedUserPages(([userId], null));
        StubEmailsForAllUsers();

        // Per-type opt-out (теоретически возможен через PUT /preferences с кодом 25):
        // критичное уведомление об аккаунте им не глушится.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IUserOptOutsRepository repo = scope.ServiceProvider.GetRequiredService<IUserOptOutsRepository>();
            await repo.ReplaceAsync(userId, [NotificationType.EmailLoginNotice]);
        }

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();
        Assert.Equal(1, run.Queued);

        int noticeCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.EmailLoginNotice && n.RecipientUserId == userId));
        Assert.Equal(1, noticeCount);
    }

    [Fact]
    public async Task Run_SecondTimeImmediately_NoDuplicateNotices()
    {
        Guid userA = Guid.NewGuid();
        StubGithubLinkedUserPages(([userA], null));
        StubEmailsForAllUsers();

        AuthenticateAsAdmin();

        RunCampaignResponse first = await PostRunAsync();
        Assert.Equal(1, first.Queued);

        // Повторный проход сразу: A уже уведомлён под фиксированным campaign-correlation →
        // unique-индекс гасит дубликат (queued по-прежнему считает адресата прохода).
        RunCampaignResponse second = await PostRunAsync();
        Assert.Equal(1, second.Queued);

        int noticeCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.EmailLoginNotice && n.RecipientUserId == userA));
        Assert.Equal(1, noticeCount);
    }

    [Fact]
    public async Task Run_UserWithoutResolvableEmail_SkippedButOthersQueued()
    {
        Guid withEmail = Guid.NewGuid();
        Guid withoutEmail = Guid.NewGuid();

        StubGithubLinkedUserPages(([withEmail, withoutEmail], null));
        // Batch-lookup возвращает почту только одному — второй без email (не должно
        // случаться в prod-аудитории, но «Введите эту почту: » слать нельзя).
        AuthServiceClient
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                ci.Arg<IReadOnlyList<Guid>>()
                    .Where(id => id == withEmail)
                    .Select(id => new AuthUserLookupDto(id, "User", "user", EmailOf(id), null))
                    .ToList()));

        AuthenticateAsAdmin();
        RunCampaignResponse run = await PostRunAsync();

        Assert.Equal(1, run.Queued);

        List<Guid> recipients = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.EmailLoginNotice)
            .Select(n => n.RecipientUserId)
            .ToListAsync());
        Assert.Equal([withEmail], recipients);
    }

    [Fact]
    public async Task Test_DispatchesNoticeOnlyToCallingAdmin()
    {
        Guid admin = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        StubEmailsForAllUsers();

        AuthenticateAsAdmin(admin);
        SendTestCampaignResponse resp = await PostTestAsync();

        Assert.True(resp.Sent);

        int adminNotices = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.EmailLoginNotice && n.RecipientUserId == admin));
        Assert.Equal(1, adminNotices);

        // Никому, кроме вызывающего админа, тестовая отправка не уходит.
        int otherNotices = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.EmailLoginNotice && n.RecipientUserId == otherUser));
        Assert.Equal(0, otherNotices);

        int totalNotices = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.EmailLoginNotice));
        Assert.Equal(1, totalNotices);
    }

    [Fact]
    public async Task RecipientCount_ReturnsSumOfGithubLinkedPages()
    {
        Guid u1 = Guid.NewGuid();
        Guid u2 = Guid.NewGuid();
        Guid u3 = Guid.NewGuid();

        // Три пользователя на двух keyset-страницах githubLinked-аудитории.
        StubGithubLinkedUserPages(([u1, u2], u2), ([u3], null));

        AuthenticateAsAdmin();
        CampaignRecipientCountResponse resp = await GetRecipientCountAsync();

        Assert.Equal(3, resp.Count);

        await AuthServiceClient.Received().GetAllUserIdsAsync(
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == true), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RUN_PATH, "POST")]
    [InlineData(TEST_PATH, "POST")]
    [InlineData(RECIPIENT_COUNT_PATH, "GET")]
    public async Task Endpoints_NonAdmin_Forbidden(string path, string method)
    {
        AuthenticateAs(Guid.NewGuid()); // обычный user без Platform.ADMIN permission

        HttpResponseMessage resp = string.Equals(method, "POST", StringComparison.Ordinal)
            ? await AppHttpClient.PostAsync(new Uri(path, UriKind.Relative), content: null)
            : await AppHttpClient.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // -- helpers --

    private static string EmailOf(Guid userId) => $"{userId}@test.com";

    /// <summary>
    ///     Стабит keyset-страницы githubLinked-аудитории AuthService: ключ — afterId, с которым
    ///     придёт runner. Любой другой запрос (в т.ч. с неверным githubLinked-флагом) получает
    ///     пустую страницу — кампания с неправильной аудиторией никого не уведомит и тест упадёт.
    /// </summary>
    private void StubGithubLinkedUserPages(params (Guid[] UserIds, Guid? NextAfterId)[] pages)
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
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Is<bool?>(g => g == true), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                Guid key = ci.Arg<Guid?>() ?? Guid.Empty;
                AllUserIdsResponse page = byAfterId.TryGetValue(key, out AllUserIdsResponse? found)
                    ? found
                    : new AllUserIdsResponse([], null);
                return Result.Success<AllUserIdsResponse, Error>(page);
            });
    }

    /// <summary>Batch-lookup: каждому id — детерминированная почта <c>{id}@test.com</c>.</summary>
    private void StubEmailsForAllUsers()
    {
        AuthServiceClient
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                ci.Arg<IReadOnlyList<Guid>>()
                    .Select(id => new AuthUserLookupDto(id, "User", "user", EmailOf(id), null))
                    .ToList()));
    }

    private async Task<RunCampaignResponse> PostRunAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(new Uri(RUN_PATH, UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<RunCampaignResponse>(resp);
    }

    private async Task<SendTestCampaignResponse> PostTestAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(new Uri(TEST_PATH, UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<SendTestCampaignResponse>(resp);
    }

    private async Task<CampaignRecipientCountResponse> GetRecipientCountAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync(new Uri(RECIPIENT_COUNT_PATH, UriKind.Relative));
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