using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using SharedKernel;

namespace NotificationService.Core.Features.Campaigns;

/// <summary>
/// Кампания «вход теперь по почте» (#704, epic #696): сервисная рассылка
/// <see cref="Domain.Notifications.NotificationType.EmailLoginNotice"/> пользователям
/// С GitHub-привязкой (аудитория — AuthService <c>githubLinked: true</c>). GitHub-вход
/// удалён по закону; письмо объясняет, что аккаунт сохранён и вход теперь по OTP-коду
/// на почту. Запускается админом. Без новых RabbitMQ-событий — доставка через обычный
/// <see cref="INotificationDispatcher"/>. Email ФОРСИРОВАН на шаблоне
/// (<c>ForcedChannels</c>) — критичное уведомление об аккаунте идёт поверх выключенного
/// Email-канала и per-type opt-out'а.
///
/// Идемпотентность per-user: фиксированный <see cref="CAMPAIGN_ID"/> даёт детерминированный
/// correlation на пару (campaign × userId) — повторный запуск пропускает уже-уведомлённых.
/// </summary>
public interface IEmailLoginNoticeCampaignRunner
{
    /// <summary>
    /// Один проход кампании: рассылает уведомление всем пользователям с GitHub-привязкой.
    /// Идемпотентен per-user. Пользователи без резолвящегося email пропускаются (в аудитории
    /// их быть не должно — GitHub-регистрация требовала email; см. прод-проверку в design §5).
    /// </summary>
    /// <returns>Количество поставленных в очередь уведомлений (адресатов прохода).</returns>
    Task<Result<int, Error>> RunAsync(CancellationToken ct = default);

    /// <summary>
    /// Тестовая отправка ОДНОГО уведомления только указанному админу — с НЕ-campaign
    /// correlation (random), чтобы не пометить админа уведомлённым и можно было повторять.
    /// </summary>
    Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Размер адресуемой аудитории (сумма всех id с GitHub-привязкой из keyset-пагинации
    /// AuthService). Канальная фильтрация здесь не применяется.
    /// </summary>
    Task<Result<int, Error>> CountRecipientsAsync(CancellationToken ct = default);
}

public sealed class EmailLoginNoticeCampaignRunner : IEmailLoginNoticeCampaignRunner
{
    /// <summary>
    /// Фиксированный well-known id кампании (#704). Входит в per-user correlation, обеспечивая
    /// детерминированную идемпотентность: re-run не плодит дубликаты.
    /// </summary>
    private static readonly Guid CAMPAIGN_ID = new("0197b000-0000-7000-8000-000000000704");

    /// <summary>
    /// Контакт поддержки в Telegram — тот же, что захардкожен в остальных email-шаблонах
    /// платформы (<c>plan-grant-received.html</c>, <c>access-expired.html</c>, ...).
    /// </summary>
    private const string SUPPORT_TG_URL = "https://t.me/sachkov_blog";

    /// <summary>
    /// Размер keyset-страницы id пользователей из AuthService (как у level-test кампании).
    /// Не больше 500 — лимит валидатора batch-endpoint'а <c>/internal/users/batch</c>,
    /// которым страница целиком резолвится в email'ы.
    /// </summary>
    private const int USERS_PAGE_SIZE = 500;

    /// <summary>Чанк диспатча — та же причина, что у дайджеста (peak memory: транзакция + outbox).</summary>
    private const int DISPATCH_BATCH_SIZE = 25;

    private readonly INotificationDispatcher _dispatcher;
    private readonly IAuthServiceClient _authClient;
    private readonly NotificationOptions _options;
    private readonly ILogger<EmailLoginNoticeCampaignRunner> _logger;

    public EmailLoginNoticeCampaignRunner(
        INotificationDispatcher dispatcher,
        IAuthServiceClient authClient,
        IOptions<NotificationOptions> options,
        ILogger<EmailLoginNoticeCampaignRunner> logger)
    {
        _dispatcher = dispatcher;
        _authClient = authClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<int, Error>> RunAsync(CancellationToken ct = default)
    {
        int queued = 0;
        int skippedNoEmail = 0;
        List<NotificationRequest> chunk = new(DISPATCH_BATCH_SIZE);
        Guid? afterId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Result<AllUserIdsResponse, Error> page =
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: true, ct);

            if (page.IsFailure)
            {
                // Частичная рассылка не страшна: per-user идемпотентность покрывает
                // повторный прогон — уже уведомлённые будут пропущены.
                _logger.LogError(
                    "Email-login-notice campaign: AuthService user-ids page failed after {Queued} users ({Error})",
                    queued, page.Error.Messages[0].Message);
                return page.Error;
            }

            if (page.Value.UserIds.Count > 0)
            {
                // Batch-резолв login-почт страницы: {email} — обязательный аргумент и InApp-,
                // и email-шаблона («Введите эту почту: …»).
                Result<IReadOnlyDictionary<Guid, string>, Error> emails =
                    await ResolveEmailsAsync(page.Value.UserIds, ct);
                if (emails.IsFailure)
                {
                    _logger.LogError(
                        "Email-login-notice campaign: email batch-lookup failed after {Queued} users ({Error})",
                        queued, emails.Error.Messages[0].Message);
                    return emails.Error;
                }

                foreach (Guid userId in page.Value.UserIds)
                {
                    if (!emails.Value.TryGetValue(userId, out string? email))
                    {
                        // Не должно случаться (GitHub-регистрация требовала email) — прод-чек
                        // в design §5. Молча слать «Введите эту почту: » нельзя — пропускаем.
                        skippedNoEmail++;
                        _logger.LogWarning(
                            "Email-login-notice campaign: user {UserId} has no resolvable email — skipped",
                            userId);
                        continue;
                    }

                    chunk.Add(BuildRequest(userId, CorrelationIds.Combine(CAMPAIGN_ID, userId), email));
                    queued++;

                    if (chunk.Count >= DISPATCH_BATCH_SIZE)
                    {
                        await _dispatcher.DispatchAsync(chunk, ct);
                        chunk.Clear();
                    }
                }
            }

            if (page.Value.NextAfterId is null)
                break;

            afterId = page.Value.NextAfterId;
        }

        if (chunk.Count > 0)
            await _dispatcher.DispatchAsync(chunk, ct);

        _logger.LogInformation(
            "Email-login-notice campaign: queued notices to {Queued} users, skipped {Skipped} without email",
            queued, skippedNoEmail);

        return queued;
    }

    public async Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default)
    {
        Result<IReadOnlyDictionary<Guid, string>, Error> emails =
            await ResolveEmailsAsync([adminUserId], ct);
        if (emails.IsFailure)
            return emails.Error;

        if (!emails.Value.TryGetValue(adminUserId, out string? email))
            return Error.NotFound(
                "campaign.admin.email.not.found",
                "У текущего администратора не найдена почта для тестовой отправки.");

        // Random correlation — тестовая отправка не помечает админа уведомлённым в реальной
        // кампании и может повторяться.
        NotificationRequest request = BuildRequest(adminUserId, Guid.CreateVersion7(), email);

        await _dispatcher.DispatchAsync(request, ct);

        _logger.LogInformation(
            "Email-login-notice campaign: test notice dispatched to admin {AdminUserId}", adminUserId);

        return 1;
    }

    public async Task<Result<int, Error>> CountRecipientsAsync(CancellationToken ct = default)
    {
        int count = 0;
        Guid? afterId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Result<AllUserIdsResponse, Error> page =
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: true, ct);

            if (page.IsFailure)
            {
                _logger.LogError(
                    "Email-login-notice campaign: recipient-count page failed after {Count} users ({Error})",
                    count, page.Error.Messages[0].Message);
                return page.Error;
            }

            count += page.Value.UserIds.Count;

            if (page.Value.NextAfterId is null)
                break;

            afterId = page.Value.NextAfterId;
        }

        return count;
    }

    private async Task<Result<IReadOnlyDictionary<Guid, string>, Error>> ResolveEmailsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> users =
            await _authClient.GetUsersByIdsAsync(userIds, ct);
        if (users.IsFailure)
            return users.Error;

        Dictionary<Guid, string> map = new(users.Value.Count);
        foreach (AuthUserLookupDto user in users.Value)
        {
            if (!string.IsNullOrWhiteSpace(user.Email))
                map[user.UserId] = user.Email;
        }

        return map;
    }

    private NotificationRequest BuildRequest(Guid userId, Guid correlationId, string email) =>
        NotificationRequest.From(
            template: NotificationTemplates.EmailLoginNotice,
            recipientUserId: userId,
            // email — user-derived, escape'ится per-channel штатно. loginUrl/supportTg — raw:
            // server-controlled config-URL'ы (не user input), HtmlEncode сломал бы href.
            // frontendUrl нужен email-layout'у (_layout.html подставляет {frontendUrl}).
            args: TemplateArgs.Of(
                    ("frontendUrl", _options.FrontendBaseUrl),
                    ("email", email))
                .WithRaw("loginUrl", BuildLoginUrl())
                .WithRaw("supportTg", SUPPORT_TG_URL),
            correlationId: correlationId);

    private string BuildLoginUrl() =>
        $"{_options.FrontendBaseUrl.TrimEnd('/')}/login";
}
