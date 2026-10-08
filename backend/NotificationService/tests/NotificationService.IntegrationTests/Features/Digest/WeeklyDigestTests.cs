using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Digest;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Digest;

/// <summary>
///     Еженедельный дайджест (#468; глобальная модель #532) —
///     <c>POST /notifications/admin/digest/run</c> + <c>WeeklyDigestRunner</c>. Проверяем:
///     <list type="bullet">
///         <item>Endpoint закрыт permission Platform.ADMIN (обычный user → 403)</item>
///         <item>При наличии нового контента в ECS дайджест получает КАЖДЫЙ пользователь
///         из AuthService (включая не подписанного ни на что), keyset-пагинация по страницам;
///         тело содержит счётчики и заголовки; payload — materialsCount/coursesCount</item>
///         <item>Дайджест создаётся для Inbox + Telegram без вызова EmailSender (#802)</item>
///         <item>Повторный триггер сразу после первого — без дубликата
///         (per-user окно идемпотентности 6 дней)</item>
///         <item>Нет нового контента → проход пропускается, ничего не создаётся</item>
///     </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class WeeklyDigestTests : NotificationServiceTestsBase
{
    public WeeklyDigestTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RunDigest_NonAdmin_Forbidden()
    {
        AuthenticateAs(Guid.NewGuid()); // обычный user без Admin permission

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            new Uri("/notifications/admin/digest/run", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task RunDigest_NewContent_AllUsersAcrossPagesGetSingleDigest()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();
        Guid courseMaterialId = Guid.NewGuid();
        Guid standaloneMaterialId = Guid.NewGuid();

        StubDigestContent(
            materials:
            [
                new DigestMaterialDto(courseMaterialId, "Дженерики в C#", "dotnet", ".NET Fullstack", DateTime.UtcNow.AddDays(-1)),
                new DigestMaterialDto(standaloneMaterialId, "Span и память", null, null, DateTime.UtcNow.AddDays(-2)),
            ],
            courses:
            [
                new DigestCourseDto(Guid.NewGuid(), "Алгоритмы", "algo", "COURSE", DateTime.UtcNow.AddDays(-3)),
            ]);

        // Две keyset-страницы: [A, B] → NextAfterId=B, [C] → конец.
        StubUserPages(([userA, userB], userB), ([userC], null));

        AuthenticateAsAdmin();
        RunDigestResponse run = await TriggerDigestAsync();

        Assert.Equal(3, run.UsersNotified);

        List<Notification> digests = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.Type == NotificationType.WeeklyDigest)
            .ToListAsync());

        Assert.Equal(3, digests.Count);
        Assert.Equal(
            new[] { userA, userB, userC }.Order().ToArray(),
            digests.Select(n => n.RecipientUserId).Order().ToArray());
        Assert.All(digests,
            notification => Assert.Equal(
                NotificationChannel.InApp | NotificationChannel.Telegram,
                notification.Channels));

        Notification digest = digests[0];
        Assert.Equal("Что нового за неделю", digest.Title);

        // Счётчики + заголовки (курсы первыми) в InApp-теле.
        Assert.Contains("2 новых материала", digest.Body, StringComparison.Ordinal);
        Assert.Contains("1 новый курс", digest.Body, StringComparison.Ordinal);
        Assert.Contains("Новый курс «Алгоритмы»", digest.Body, StringComparison.Ordinal);
        Assert.Contains("«Дженерики в C#» — в курсе «.NET Fullstack»", digest.Body, StringComparison.Ordinal);
        Assert.Contains("«Span и память»", digest.Body, StringComparison.Ordinal);

        // Deep-link дайджеста ведёт на /home (PlatformLinkBuilder запекает targetUrl в payload).
        using JsonDocument payloadDoc = JsonDocument.Parse(digest.Payload);
        Assert.EndsWith("/home", payloadDoc.RootElement.GetProperty("targetUrl").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(2, payloadDoc.RootElement.GetProperty("materialsCount").GetInt32());
        Assert.Equal(1, payloadDoc.RootElement.GetProperty("coursesCount").GetInt32());

        await EmailSender.DidNotReceiveWithAnyArgs()
            .SendAsync(default!, default!, default!, default!, default, default, default);
        await AuthServiceClient.DidNotReceiveWithAnyArgs()
            .GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task RunDigest_SecondTriggerImmediately_NoDuplicate()
    {
        Guid userA = Guid.NewGuid();

        StubDigestContent(
            materials:
            [
                new DigestMaterialDto(Guid.NewGuid(), "Новый материал", "dotnet", ".NET Fullstack", DateTime.UtcNow.AddDays(-1)),
            ],
            courses: []);
        StubUserPages(([userA], null));

        AuthenticateAsAdmin();

        RunDigestResponse first = await TriggerDigestAsync();
        Assert.Equal(1, first.UsersNotified);

        // Повторный проход сразу: A уже получил WeeklyDigest < 6 дней назад → пропуск.
        RunDigestResponse second = await TriggerDigestAsync();
        Assert.Equal(0, second.UsersNotified);

        int digestCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.WeeklyDigest && n.RecipientUserId == userA));
        Assert.Equal(1, digestCount);
    }

    [Fact]
    public async Task RunDigest_NoNewContent_SkipsEntirely()
    {
        StubDigestContent(materials: [], courses: []);
        StubUserPages(([Guid.NewGuid()], null));

        AuthenticateAsAdmin();
        RunDigestResponse run = await TriggerDigestAsync();

        Assert.Equal(0, run.UsersNotified);

        int digestCount = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.Type == NotificationType.WeeklyDigest));
        Assert.Equal(0, digestCount);

        // Пустая неделя → за пользователями даже не ходим.
        await AuthServiceClient.DidNotReceiveWithAnyArgs()
            .GetAllUserIdsAsync(default, default, default, default);
    }

    // -- helpers --

    private void StubDigestContent(
        IReadOnlyList<DigestMaterialDto> materials,
        IReadOnlyList<DigestCourseDto> courses)
    {
        EducationContentClient
            .GetDigestContentAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DigestContentDto, Error>(new DigestContentDto(materials, courses)));
    }

    /// <summary>
    ///     Стабит keyset-страницы AuthService: ключ — afterId, с которым придёт runner
    ///     (null для первой страницы, NextAfterId предыдущей — для следующих).
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

    private async Task<RunDigestResponse> TriggerDigestAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            new Uri("/notifications/admin/digest/run", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<RunDigestResponse> envelope =
            (await resp.Content.ReadFromJsonAsync<Envelope<RunDigestResponse>>())!;
        Assert.False(envelope.IsError, $"Envelope error: {envelope.Error?.Code}");
        return envelope.Result!;
    }

    private sealed class RunDigestResponse
    {
        public int UsersNotified { get; init; }
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
