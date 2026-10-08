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
/// Кампания «приглашение пройти тест уровня» (#554): сервисная рассылка
/// <see cref="Domain.Notifications.NotificationType.LevelTestInvite"/> ВСЕМ зарегистрированным
/// пользователям (страницы id из AuthService), запускается админом. Без новых RabbitMQ-событий —
/// доставка идёт через обычный <see cref="INotificationDispatcher"/>, так что канальные флаги
/// и per-type opt-out юзера применяются стандартно.
///
/// Идемпотентность per-user: фиксированный <see cref="CAMPAIGN_ID"/> даёт детерминированный
/// correlation на пару (campaign × userId) — повторный запуск пропускает уже-приглашённых
/// (notifications дедуплицируются на unique-индексе correlation, как у дайджеста).
/// </summary>
public interface ILevelTestInviteCampaignRunner
{
    /// <summary>
    /// Один проход кампании: рассылает приглашение всем пользователям. Идемпотентен per-user.
    /// </summary>
    /// <returns>Количество поставленных в очередь уведомлений (адресатов прохода).</returns>
    Task<Result<int, Error>> RunAsync(CancellationToken ct = default);

    /// <summary>
    /// Тестовая отправка ОДНОГО приглашения только указанному админу — с НЕ-campaign correlation
    /// (random), чтобы не пометить админа приглашённым в реальной кампании и можно было повторять.
    /// </summary>
    Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Размер адресуемой аудитории (сумма всех id из keyset-пагинации AuthService).
    /// Opt-out / выключенный Email-канал фильтруются уже на доставке, не здесь.
    /// </summary>
    Task<Result<int, Error>> CountRecipientsAsync(CancellationToken ct = default);
}

public sealed class LevelTestInviteCampaignRunner : ILevelTestInviteCampaignRunner
{
    /// <summary>
    /// Фиксированный well-known id кампании (#554). Входит в per-user correlation, обеспечивая
    /// детерминированную идемпотентность: re-run не плодит дубликаты приглашений.
    /// </summary>
    private static readonly Guid CAMPAIGN_ID = new("0197b000-0000-7000-8000-000000000554");

    /// <summary>Размер keyset-страницы id пользователей из AuthService (как у дайджеста).</summary>
    private const int USERS_PAGE_SIZE = 500;

    /// <summary>
    /// Чанк диспатча — та же причина, что у дайджеста (peak memory: per-request транзакция
    /// + outbox buffer).
    /// </summary>
    private const int DISPATCH_BATCH_SIZE = 25;

    private readonly INotificationDispatcher _dispatcher;
    private readonly IAuthServiceClient _authClient;
    private readonly NotificationOptions _options;
    private readonly ILogger<LevelTestInviteCampaignRunner> _logger;

    public LevelTestInviteCampaignRunner(
        INotificationDispatcher dispatcher,
        IAuthServiceClient authClient,
        IOptions<NotificationOptions> options,
        ILogger<LevelTestInviteCampaignRunner> logger)
    {
        _dispatcher = dispatcher;
        _authClient = authClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<int, Error>> RunAsync(CancellationToken ct = default)
    {
        string levelTestUrl = BuildLevelTestUrl();

        int queued = 0;
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
                // повторный прогон — уже приглашённые будут пропущены.
                _logger.LogError(
                    "Level-test invite campaign: AuthService user-ids page failed after {Queued} users ({Error})",
                    queued, page.Error.Messages[0].Message);
                return page.Error;
            }

            foreach (Guid userId in page.Value.UserIds)
            {
                chunk.Add(BuildRequest(userId, CorrelationIds.Combine(CAMPAIGN_ID, userId), levelTestUrl));
                queued++;

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
            "Level-test invite campaign: queued invitations to {Queued} users", queued);

        return queued;
    }

    public async Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default)
    {
        // Random correlation — тестовая отправка не помечает админа приглашённым в реальной
        // кампании и может повторяться.
        NotificationRequest request =
            BuildRequest(adminUserId, Guid.CreateVersion7(), BuildLevelTestUrl());

        await _dispatcher.DispatchAsync(request, ct);

        _logger.LogInformation(
            "Level-test invite campaign: test invitation dispatched to admin {AdminUserId}", adminUserId);

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
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: null, ct);

            if (page.IsFailure)
            {
                _logger.LogError(
                    "Level-test invite campaign: recipient-count page failed after {Count} users ({Error})",
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

    private NotificationRequest BuildRequest(Guid userId, Guid correlationId, string levelTestUrl) =>
        NotificationRequest.From(
            template: NotificationTemplates.LevelTestInvite,
            recipientUserId: userId,
            // Имена per-user не резолвим (как дайджест) — generic greeting; email/InApp работают
            // без персонализации, лишний S2S-fan-out на AuthService не нужен.
            // displayName — обычный (escaped) arg; frontendUrl нужен email-layout'у (_layout.html
            // подставляет {frontendUrl} в logo/footer ссылки). levelTestUrl — raw: это
            // server-controlled config (не user input), HtmlEncode сломал бы href в CTA-кнопке.
            args: TemplateArgs.Of(
                    ("displayName", "Привет!"),
                    ("frontendUrl", _options.FrontendBaseUrl))
                .WithRaw("levelTestUrl", levelTestUrl),
            correlationId: correlationId);

    private string BuildLevelTestUrl() =>
        $"{_options.FrontendBaseUrl.TrimEnd('/')}/level-test";
}
