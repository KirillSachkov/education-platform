using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NotificationService.Contracts.Preferences.Dtos;
using NotificationService.Contracts.Preferences.Requests;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Preferences;

/// <summary>
///     End-to-end покрытие <c>PUT /notifications/preferences/</c> через HTTP — раньше тестами
///     был покрыт только <c>ReplaceAsync</c> на уровне репозитория, а сам endpoint с непустым
///     <c>OptedOutTypes</c> — нет. Из-за этого пропустили баг #689: handler звал
///     <c>Enum.IsDefined(typeof(NotificationType), code)</c> с <c>short</c> (Int16) против
///     enum'а с underlying <c>Int32</c> → <c>ArgumentException</c> → 500 при отключении ЛЮБОГО
///     типа уведомления (пустой набор не падал — foreach просто не выполнялся).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class UpdatePreferencesTests : NotificationServiceTestsBase
{
    public UpdatePreferencesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PutPreferences_WithOptedOutTypes_Returns200_PersistsAndEchoesBack()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        UpdatePreferencesRequest body = new()
        {
            TelegramEnabled = true,
            EmailEnabled = true,
            WebPushEnabled = true,
            OptedOutTypes = [(short)NotificationType.CourseEnrolled, (short)NotificationType.MaterialPublished],
        };

        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Эхо в ответе отражает сохранённый набор (отсортирован по возрастанию).
        NotificationPreferenceDto dto = await ReadResultAsync(resp);
        Assert.Equal(
            new short[] { (short)NotificationType.CourseEnrolled, (short)NotificationType.MaterialPublished },
            dto.OptedOutTypes);

        // И реально лежит в БД (GetOptedOutFromDb уже сортирует по возрастанию).
        List<NotificationType> persisted = await GetOptedOutFromDb(userId);
        Assert.Equal(
            new[] { NotificationType.CourseEnrolled, NotificationType.MaterialPublished },
            persisted);
    }

    [Fact]
    public async Task PutPreferences_WithUnknownCode_IgnoresUnknown_Returns200()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        // 9999 — не существует в NotificationType. Forward-compat: сервер игнорирует, не падает.
        UpdatePreferencesRequest body = new()
        {
            TelegramEnabled = true,
            EmailEnabled = true,
            OptedOutTypes = [(short)NotificationType.MaterialPublished, (short)9999],
        };

        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        NotificationPreferenceDto dto = await ReadResultAsync(resp);
        Assert.Equal(new short[] { (short)NotificationType.MaterialPublished }, dto.OptedOutTypes);

        List<NotificationType> persisted = await GetOptedOutFromDb(userId);
        Assert.Equal(new[] { NotificationType.MaterialPublished }, persisted);
    }

    [Fact]
    public async Task PutPreferences_EmptyOptedOutTypes_ClearsExisting_Returns200()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        UpdatePreferencesRequest seed = new()
        {
            TelegramEnabled = true,
            EmailEnabled = true,
            OptedOutTypes = [(short)NotificationType.CommentReplied, (short)NotificationType.IssuePublished],
        };
        HttpResponseMessage seedResp = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), seed);
        Assert.Equal(HttpStatusCode.OK, seedResp.StatusCode);
        Assert.Equal(2, (await GetOptedOutFromDb(userId)).Count);

        // Пустой набор = снять все opt-out'ы (replace-семантика). Не должен ломаться.
        UpdatePreferencesRequest clear = new()
        {
            TelegramEnabled = true,
            EmailEnabled = true,
            OptedOutTypes = [],
        };
        HttpResponseMessage clearResp = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), clear);

        Assert.Equal(HttpStatusCode.OK, clearResp.StatusCode);

        NotificationPreferenceDto dto = await ReadResultAsync(clearResp);
        Assert.Empty(dto.OptedOutTypes);
        Assert.Empty(await GetOptedOutFromDb(userId));
    }

    [Fact]
    public async Task PutPreferences_PersistsChannelFlags_Returns200()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId);

        UpdatePreferencesRequest body = new()
        {
            TelegramEnabled = false,
            EmailEnabled = false,
            WebPushEnabled = false,
            OptedOutTypes = [],
        };

        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            new Uri("/notifications/preferences", UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        NotificationPreferenceDto dto = await ReadResultAsync(resp);
        Assert.False(dto.TelegramEnabled);
        Assert.False(dto.EmailEnabled);
        Assert.False(dto.WebPushEnabled);

        bool flagsPersisted = await ExecuteInDb(db => db.UserNotificationChannels
            .AsNoTracking()
            .AnyAsync(x => x.UserId == userId
                && !x.TelegramEnabled && !x.EmailEnabled && !x.WebPushEnabled));
        Assert.True(flagsPersisted);
    }

    private static async Task<NotificationPreferenceDto> ReadResultAsync(HttpResponseMessage resp)
    {
        Envelope<NotificationPreferenceDto>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<NotificationPreferenceDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        return envelope.Result!;
    }

    private Task<List<NotificationType>> GetOptedOutFromDb(Guid userId) =>
        ExecuteInDb(db => db.UserNotificationTypeOptOuts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Type)
            .OrderBy(x => x)
            .ToListAsync());
}
