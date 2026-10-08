using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Dapper;
using EducationContentService.Contracts.Digest;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Core.Features.Digest;

/// <summary>
/// Один проход еженедельного дайджеста (#468; глобальная модель — #532): берёт «что нового
/// на платформе» из ECS (опубликованные за 7 дней материалы и курсы) и рассылает по одному
/// <see cref="NotificationType.WeeklyDigest"/> КАЖДОМУ зарегистрированному пользователю
/// (страницы id из AuthService). Элементы дайджеста несут прямые ссылки в Telegram
/// (MarkdownV1 raw-аргументы, #532). Никаких новых RabbitMQ-событий —
/// доставка идёт через обычный <see cref="INotificationDispatcher"/>, так что канальные
/// флаги и per-type opt-out юзера применяются стандартно.
/// </summary>
public interface IWeeklyDigestRunner
{
    /// <summary>
    /// Watermark последнего глобального запуска — <c>MAX(created_at)</c> уведомлений
    /// с <c>type = WeeklyDigest</c>. <c>null</c> → дайджест ещё ни разу не рассылался.
    /// Отдельной таблицы состояния нет — миграция не нужна.
    /// </summary>
    Task<DateTime?> GetLastGlobalRunAtAsync(CancellationToken ct = default);

    /// <summary>
    /// Выполняет один проход дайджеста немедленно (без schedule-guard'а — его держит
    /// <c>WeeklyDigestService</c>; ручной admin-триггер зовёт напрямую).
    /// Нет нового контента за окно → проход пропускается (пустые дайджесты не шлём).
    /// Идемпотентность per-user: получатели с WeeklyDigest за последние 6 дней пропускаются.
    /// </summary>
    /// <returns>Количество пользователей, которым отправлен дайджест.</returns>
    Task<int> RunOnceAsync(CancellationToken ct = default);
}

public sealed class WeeklyDigestRunner : IWeeklyDigestRunner
{
    /// <summary>Окно агрегации новостей — последние 7 дней.</summary>
    private const int SOURCE_WINDOW_DAYS = 7;

    /// <summary>
    /// Per-user окно идемпотентности: получивший WeeklyDigest за последние 6 дней
    /// пропускается. 6 (не 7) — допуск на дрейф времени запуска между неделями.
    /// </summary>
    private const int IDEMPOTENCY_WINDOW_DAYS = 6;

    /// <summary>Максимум строк-элементов в теле дайджеста (курсы первыми, потом материалы).</summary>
    private const int MAX_ITEMS_PER_DIGEST = 8;

    /// <summary>Сколько элементов каждого вида запрашиваем у ECS (для честных счётчиков).</summary>
    private const int MAX_ITEMS_PER_KIND = 20;

    /// <summary>Размер keyset-страницы id пользователей из AuthService.</summary>
    private const int USERS_PAGE_SIZE = 500;

    /// <summary>
    /// Чанк диспатча — та же причина, что у <c>MaterialPublishedHandler.BATCH_SIZE</c>
    /// (peak memory: per-request транзакция + outbox buffer, issue #67/#230).
    /// </summary>
    private const int DISPATCH_BATCH_SIZE = 25;

    private const int QUERY_TIMEOUT_SECONDS = 30;

    private readonly ITransactionManager _transactions;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly NotificationOptions _options;
    private readonly ILogger<WeeklyDigestRunner> _logger;

    public WeeklyDigestRunner(
        ITransactionManager transactions,
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        IOptions<NotificationOptions> options,
        ILogger<WeeklyDigestRunner> logger)
    {
        _transactions = transactions;
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DateTime?> GetLastGlobalRunAtAsync(CancellationToken ct = default)
    {
        DbConnection conn = _transactions.GetDbConnection();

        return await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            """
            SELECT MAX(created_at)
            FROM notifications.notifications
            WHERE type = @digestType
            """,
            new { digestType = (short)NotificationType.WeeklyDigest },
            commandTimeout: QUERY_TIMEOUT_SECONDS,
            cancellationToken: ct));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime sinceUtc = nowUtc.AddDays(-SOURCE_WINDOW_DAYS);
        DateTime idempotencyCutoff = nowUtc.AddDays(-IDEMPOTENCY_WINDOW_DAYS);

        Result<DigestContentDto, Error> content =
            await _ecsClient.GetDigestContentAsync(sinceUtc, MAX_ITEMS_PER_KIND, ct);

        if (content.IsFailure)
        {
            // ECS недоступен → проход пропускаем целиком (watermark не сдвигается,
            // следующий тик WeeklyDigestService попробует снова).
            _logger.LogWarning(
                "Weekly digest pass: ECS digest-content unavailable, skipping ({Error})",
                content.Error.Messages[0].Message);
            return 0;
        }

        if (content.Value.Materials.Count == 0 && content.Value.Courses.Count == 0)
        {
            _logger.LogInformation(
                "Weekly digest pass: no new published content since {Since:u}, skipping", sinceUtc);
            return 0;
        }

        DigestBody body = BuildBody(content.Value, _options.FrontendBaseUrl);

        HashSet<Guid> alreadyNotified = await LoadRecentDigestRecipientsAsync(idempotencyCutoff, ct);

        // Стабильная корреляция на (день запуска): unique-индекс
        // (correlation_id, recipient_user_id, type) дополнительно гасит гонку
        // одновременных проходов (scheduled + manual в один момент).
        Guid correlationId = DigestCorrelation(DateOnly.FromDateTime(nowUtc));

        int usersNotified = 0;
        List<NotificationRequest> chunk = new(DISPATCH_BATCH_SIZE);
        Guid? afterId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Result<AllUserIdsResponse, Error> page =
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: null, ct);

            if (page.IsFailure)
            {
                // Частичная рассылка не страшна: per-user идемпотентность покрывает
                // повторный (ручной) прогон — уже получившие будут пропущены.
                _logger.LogError(
                    "Weekly digest pass: AuthService user-ids page failed after {Sent} users ({Error})",
                    usersNotified, page.Error.Messages[0].Message);
                break;
            }

            foreach (Guid userId in page.Value.UserIds)
            {
                if (alreadyNotified.Contains(userId))
                    continue;

                chunk.Add(NotificationRequest.From(
                    template: NotificationTemplates.WeeklyDigest,
                    recipientUserId: userId,
                    correlationId: correlationId,
                    args: TemplateArgs.Of(
                            ("digestSummary", body.Summary),
                            ("digestItems", body.ItemsPlain))
                        .WithRaw("digestItemsTg", body.ItemsTelegram)
                        .WithRaw("digestItemsHtml", body.ItemsHtml),
                    payload: new
                    {
                        materialsCount = body.MaterialsCount,
                        coursesCount = body.CoursesCount,
                    }));
                usersNotified++;

                if (chunk.Count >= DISPATCH_BATCH_SIZE)
                {
                    await _dispatcher.DispatchAsync(chunk, ct);
                    chunk.Clear();
                }
            }

            if (page.Value.NextAfterId is null)
                break;

            afterId = page.Value.NextAfterId;
        }

        if (chunk.Count > 0)
            await _dispatcher.DispatchAsync(chunk, ct);

        _logger.LogInformation(
            "Weekly digest pass: dispatched digests to {Count} users " +
            "({Materials} new materials, {Courses} new courses since {Since:u})",
            usersNotified, body.MaterialsCount, body.CoursesCount, sinceUtc);

        return usersNotified;
    }

    /// <summary>
    /// Получатели, у которых уже есть свежий WeeklyDigest (per-user идемпотентность;
    /// повторный проход не плодит дубликаты).
    /// </summary>
    private async Task<HashSet<Guid>> LoadRecentDigestRecipientsAsync(
        DateTime idempotencyCutoff, CancellationToken ct)
    {
        DbConnection conn = _transactions.GetDbConnection();

        IEnumerable<Guid> ids = await conn.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT recipient_user_id
            FROM notifications.notifications
            WHERE type = @digestType
              AND created_at >= @idempotencyCutoff
            """,
            new
            {
                digestType = (short)NotificationType.WeeklyDigest,
                idempotencyCutoff,
            },
            commandTimeout: QUERY_TIMEOUT_SECONDS,
            cancellationToken: ct));

        return [.. ids];
    }

    /// <summary>
    /// Собирает тело дайджеста один раз на проход — контент глобальный, одинаковый для всех.
    /// Курсы первыми (запуск курса — главная новость), затем материалы; всего до
    /// <see cref="MAX_ITEMS_PER_DIGEST"/> строк. Telegram/HTML-варианты — raw-аргументы:
    /// user-derived заголовки экранируются поэлементно здесь, ссылки строим сами.
    /// </summary>
    private static DigestBody BuildBody(DigestContentDto content, string frontendBaseUrl)
    {
        string baseUrl = frontendBaseUrl.TrimEnd('/');

        List<string> plain = [];
        List<string> tg = [];
        StringBuilder html = new("<ul style=\"margin:0 0 16px 0;padding-left:20px;line-height:1.8;color:#333;\">");

        foreach (DigestCourseDto course in content.Courses)
        {
            if (plain.Count >= MAX_ITEMS_PER_DIGEST)
                break;

            string url = $"{baseUrl}/courses/{course.Slug}";
            plain.Add($"• Новый курс «{course.Title}»");
            tg.Add($"• Новый курс [«{TelegramRenderer.EscapeMarkdownV1(course.Title)}»]({url})");
            html.Append(
                CultureInfo.InvariantCulture,
                $"<li>Новый курс <a href=\"{WebUtility.HtmlEncode(url)}\" " +
                $"style=\"color:#0066cc;text-decoration:none;\">«{WebUtility.HtmlEncode(course.Title)}»</a></li>");
        }

        foreach (DigestMaterialDto material in content.Materials)
        {
            if (plain.Count >= MAX_ITEMS_PER_DIGEST)
                break;

            // In-course материал — на /courses/{slug}/learn/{id}; standalone (без привязки
            // к PUBLISHED-курсу) — на /knowledge-base/{id} (флэт /learn/{id} верхнего уровня
            // не существует как роут, #586).
            string url = material.CourseSlug is not null
                ? $"{baseUrl}/courses/{material.CourseSlug}/learn/{material.MaterialId}"
                : $"{baseUrl}/knowledge-base/{material.MaterialId}";

            string courseSuffixPlain = material.CourseTitle is not null
                ? $" — в курсе «{material.CourseTitle}»"
                : string.Empty;
            string courseSuffixTg = material.CourseTitle is not null
                ? $" — в курсе «{TelegramRenderer.EscapeMarkdownV1(material.CourseTitle)}»"
                : string.Empty;
            string courseSuffixHtml = material.CourseTitle is not null
                ? $" — в курсе «{WebUtility.HtmlEncode(material.CourseTitle)}»"
                : string.Empty;

            plain.Add($"• «{material.Title}»{courseSuffixPlain}");
            tg.Add($"• [«{TelegramRenderer.EscapeMarkdownV1(material.Title)}»]({url}){courseSuffixTg}");
            html.Append(
                CultureInfo.InvariantCulture,
                $"<li><a href=\"{WebUtility.HtmlEncode(url)}\" style=\"color:#0066cc;text-decoration:none;\">«{WebUtility.HtmlEncode(material.Title)}»</a>{courseSuffixHtml}</li>");
        }

        html.Append("</ul>");

        List<string> summaryParts = new(2);
        if (content.Materials.Count > 0)
        {
            summaryParts.Add(
                $"{content.Materials.Count} {Pluralize(content.Materials.Count, "новый материал", "новых материала", "новых материалов")}");
        }

        if (content.Courses.Count > 0)
        {
            summaryParts.Add(
                $"{content.Courses.Count} {Pluralize(content.Courses.Count, "новый курс", "новых курса", "новых курсов")}");
        }

        return new DigestBody(
            Summary: string.Join(", ", summaryParts),
            ItemsPlain: string.Join("\n", plain),
            ItemsTelegram: string.Join("\n", tg),
            ItemsHtml: html.ToString(),
            MaterialsCount: content.Materials.Count,
            CoursesCount: content.Courses.Count);
    }

    /// <summary>
    /// Детерминированный correlation-guid из даты запуска (UTC). Уникальность per-user
    /// обеспечивает сам индекс — recipient_user_id входит в его ключ.
    /// </summary>
    private static Guid DigestCorrelation(DateOnly utcDate)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, utcDate.DayNumber);
        return new Guid(bytes);
    }

    private static string Pluralize(int count, string one, string few, string many)
    {
        int mod100 = count % 100;
        if (mod100 is >= 11 and <= 14)
            return many;

        return (count % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    private sealed record DigestBody(
        string Summary,
        string ItemsPlain,
        string ItemsTelegram,
        string ItemsHtml,
        int MaterialsCount,
        int CoursesCount);
}
