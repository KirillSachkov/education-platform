using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates.Catalog;
using SharedKernel;

namespace NotificationService.Core.Features.Campaigns;

/// <summary>
/// Кампания «привяжите GitHub и Telegram» (#704, epic #696): one-shot InApp-nudge
/// <see cref="Domain.Notifications.NotificationType.LinkAccountsNudge"/> пользователям
/// БЕЗ GitHub-привязки (аудитория — AuthService <c>githubLinked: false</c>). Запускается
/// админом. Канал только InApp (мягкий продуктовый nudge, deep-link /settings/integrations);
/// канальные флаги и per-type opt-out юзера применяются стандартно.
///
/// Идемпотентность per-user: фиксированный <see cref="CAMPAIGN_ID"/> даёт детерминированный
/// correlation на пару (campaign × userId) — повторный запуск пропускает уже-уведомлённых.
/// </summary>
public interface ILinkAccountsNudgeCampaignRunner
{
    /// <summary>
    /// Один проход кампании: nudge всем пользователям без GitHub-привязки. Идемпотентен per-user.
    /// </summary>
    /// <returns>Количество поставленных в очередь уведомлений (адресатов прохода).</returns>
    Task<Result<int, Error>> RunAsync(CancellationToken ct = default);

    /// <summary>
    /// Тестовая отправка ОДНОГО nudge только указанному админу — с НЕ-campaign correlation
    /// (random), чтобы не пометить админа уведомлённым в реальной кампании и можно было повторять.
    /// </summary>
    Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Размер адресуемой аудитории (сумма всех id без GitHub-привязки из keyset-пагинации
    /// AuthService). Opt-out фильтруется уже на доставке, не здесь.
    /// </summary>
    Task<Result<int, Error>> CountRecipientsAsync(CancellationToken ct = default);
}

public sealed class LinkAccountsNudgeCampaignRunner : ILinkAccountsNudgeCampaignRunner
{
    /// <summary>
    /// Фиксированный well-known id кампании (#704; отличается от campaign-id
    /// <c>EmailLoginNoticeCampaignRunner</c>'а). Входит в per-user correlation.
    /// </summary>
    private static readonly Guid CAMPAIGN_ID = new("0197b000-0000-7000-8000-000000001704");

    /// <summary>Размер keyset-страницы id пользователей из AuthService (как у level-test кампании).</summary>
    private const int USERS_PAGE_SIZE = 500;

    /// <summary>Чанк диспатча — та же причина, что у дайджеста (peak memory: транзакция + outbox).</summary>
    private const int DISPATCH_BATCH_SIZE = 25;

    private readonly INotificationDispatcher _dispatcher;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<LinkAccountsNudgeCampaignRunner> _logger;

    public LinkAccountsNudgeCampaignRunner(
        INotificationDispatcher dispatcher,
        IAuthServiceClient authClient,
        ILogger<LinkAccountsNudgeCampaignRunner> logger)
    {
        _dispatcher = dispatcher;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task<Result<int, Error>> RunAsync(CancellationToken ct = default)
    {
        int queued = 0;
        List<NotificationRequest> chunk = new(DISPATCH_BATCH_SIZE);
        Guid? afterId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Result<AllUserIdsResponse, Error> page =
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: false, ct);

            if (page.IsFailure)
            {
                // Частичная рассылка не страшна: per-user идемпотентность покрывает
                // повторный прогон — уже уведомлённые будут пропущены.
                _logger.LogError(
                    "Link-accounts-nudge campaign: AuthService user-ids page failed after {Queued} users ({Error})",
                    queued, page.Error.Messages[0].Message);
                return page.Error;
            }

            foreach (Guid userId in page.Value.UserIds)
            {
                chunk.Add(BuildRequest(userId, CorrelationIds.Combine(CAMPAIGN_ID, userId)));
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
            "Link-accounts-nudge campaign: queued nudges to {Queued} users", queued);

        return queued;
    }

    public async Task<Result<int, Error>> RunTestAsync(Guid adminUserId, CancellationToken ct = default)
    {
        // Random correlation — тестовая отправка не помечает админа уведомлённым в реальной
        // кампании и может повторяться.
        NotificationRequest request = BuildRequest(adminUserId, Guid.CreateVersion7());

        await _dispatcher.DispatchAsync(request, ct);

        _logger.LogInformation(
            "Link-accounts-nudge campaign: test nudge dispatched to admin {AdminUserId}", adminUserId);

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
                await _authClient.GetAllUserIdsAsync(afterId, USERS_PAGE_SIZE, githubLinked: false, ct);

            if (page.IsFailure)
            {
                _logger.LogError(
                    "Link-accounts-nudge campaign: recipient-count page failed after {Count} users ({Error})",
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

    private static NotificationRequest BuildRequest(Guid userId, Guid correlationId) =>
        // Текст статичный (без placeholder'ов) — args не нужны. targetUrl запекает
        // dispatcher через PlatformLinkBuilder → /settings/integrations.
        NotificationRequest.From(
            template: NotificationTemplates.LinkAccountsNudge,
            recipientUserId: userId,
            correlationId: correlationId);
}
