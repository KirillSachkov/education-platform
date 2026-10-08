using System.Diagnostics;
using System.Text.Json;
using AuthService.Contracts.HttpCommunication;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core.Channels;
using NotificationService.Core.Database;
using NotificationService.Core.Diagnostics;
using NotificationService.Core.Notifications;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserChannels;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Shared.Navigation;
using SharedKernel;

namespace NotificationService.Core.Dispatching;

/// <summary>
/// Реализация диспатча / Notification dispatch implementation.
///
/// Алгоритм на один request:
/// <list type="number">
/// <item>Резолвим эффективные каналы: <c>requested &amp; userChannels</c>, где
/// <c>userChannels = InApp | (TelegramEnabled ? Telegram : 0) | (EmailEnabled ? Email : 0)</c>.
/// Канал <b>InApp</b> всегда включён — продуктовое решение.</item>
/// <item>Рендерим InApp-вариант для <c>Notification.Title/Body</c> (в inbox хранится InApp-текст).</item>
/// <item>Сохраняем <see cref="Notification"/> в одной транзакции с outbox'ом
/// (unique-violation на <c>correlation_id</c> → swallow как идемпотентный retry).</item>
/// <item>Для каждого включённого канала (кроме InApp) — рендерим per-channel и вызываем
/// соответствующий <see cref="INotificationChannel"/>. Ошибка канала — не откатывает основную запись.</item>
/// </list>
/// </summary>
// public (не internal) — Wolverine 6.0 code-gen на handler'ах требует возможности
// инстанцировать concrete тип через `new`. На internal sealed классе fallback'илось
// на service location, который throw'ит в 6.0.
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationsRepository _notifications;
    private readonly IUserChannelsRepository _userChannels;
    private readonly IUserOptOutsRepository _userOptOuts;
    private readonly IDeliveriesRepository _deliveries;
    private readonly IWebPushSubscriptionsRepository _webPushSubscriptions;
    private readonly ChannelRegistry _channels;
    private readonly ChannelRendererRegistry _renderers;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly IAuthServiceClient _authClient;
    private readonly NotificationOptions _options;
    private readonly NotificationMetrics _metrics;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        INotificationsRepository notifications,
        IUserChannelsRepository userChannels,
        IUserOptOutsRepository userOptOuts,
        IDeliveriesRepository deliveries,
        IWebPushSubscriptionsRepository webPushSubscriptions,
        ChannelRegistry channels,
        ChannelRendererRegistry renderers,
        ITransactionManager transactions,
        IOutboxService outbox,
        IAuthServiceClient authClient,
        IOptions<NotificationOptions> options,
        NotificationMetrics metrics,
        ILogger<NotificationDispatcher> logger)
    {
        _notifications = notifications;
        _userChannels = userChannels;
        _userOptOuts = userOptOuts;
        _deliveries = deliveries;
        _webPushSubscriptions = webPushSubscriptions;
        _channels = channels;
        _renderers = renderers;
        _transactions = transactions;
        _outbox = outbox;
        _authClient = authClient;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task DispatchAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return;

        HashSet<Guid> recipientIds = [.. requests.Select(r => r.RecipientUserId)];
        IReadOnlyDictionary<Guid, UserNotificationChannels> userChannelsMap =
            await _userChannels.GetBulkAsync(recipientIds, ct);
        IReadOnlyDictionary<Guid, IReadOnlySet<NotificationType>> optOutMap =
            await _userOptOuts.GetOptedOutBulkAsync(recipientIds, ct);

        // Pre-warm AuthService cache одним batch-запросом для всех recipients. Без этого
        // EmailNotificationChannel дёргает GetUsersByIdsAsync([userId]) на каждое уведомление —
        // на курсе с 1000 enrolled это была 1000+ HTTP-вызовов в Auth (даже если быстрые),
        // увеличивая общее время handler'а до Wolverine timeout. CachedAuthServiceClient
        // batch'ит, кеширует на 5 мин и держит уже подгретый набор для последующих
        // single-id вызовов из channel'а — те hit'ают cache (~1ms).
        if (HasEmailRecipients(requests, userChannelsMap))
        {
            // Игнорируем ошибку — на cache fail каждый Email channel сам сходит в Auth.
            _ = await _authClient.GetUsersByIdsAsync([.. recipientIds], ct);
        }

        // Web Push «eligible для всех типов» (как InApp), но только для юзеров с активной
        // подпиской устройства — иначе на каждое уведомление в аудит-лог доставок писалась бы
        // skipped-запись. Канал зарегистрирован только когда заданы VAPID-ключи; без него
        // bulk-запрос подписок не делаем (issue #342).
        NotificationChannel registeredChannels = AggregateRegisteredChannels();
        bool webPushRegistered = (registeredChannels & NotificationChannel.WebPush) != NotificationChannel.None;
        IReadOnlySet<Guid> usersWithPush = webPushRegistered
            ? await _webPushSubscriptions.GetUserIdsWithSubscriptionsAsync([.. recipientIds], ct)
            : new HashSet<Guid>();

        foreach (NotificationRequest request in requests)
        {
            ct.ThrowIfCancellationRequested();

            // Per-type opt-out: если пользователь отписан от этого типа — пропускаем полностью,
            // даже InApp. Доп. гранулярность поверх канального mask'а.
            // Исключение (#704): шаблоны с ForcedChannels — критичные уведомления об аккаунте,
            // их нельзя заглушить opt-out'ом (юзер обязан узнать, как войти в аккаунт).
            if (request.Template.ForcedChannels == NotificationChannel.None
                && optOutMap.TryGetValue(request.RecipientUserId, out IReadOnlySet<NotificationType>? optedOut)
                && optedOut.Contains(request.Type))
            {
                _logger.LogDebug(
                    "Skipping {Type} for {UserId} — user opted out of this type",
                    request.Type, request.RecipientUserId);
                continue;
            }

            NotificationChannel userMask = ResolveUserMask(request.RecipientUserId, userChannelsMap);
            NotificationChannel effective = (request.RequestedChannels & userMask) | NotificationChannel.InApp;
            // InApp всегда форсится — продуктовое решение.

            // ForcedChannels (#704): каналы критичного уведомления идут поверх канального
            // mask'а юзера (например, Email при EmailEnabled=false). Ограничены requested-
            // набором — channelsOverride в запросе всё ещё может сузить доставку.
            effective |= request.RequestedChannels & request.Template.ForcedChannels;

            // WebPush форсится для всех типов (как InApp), если юзер не выключил канал
            // (userMask несёт бит) и у него есть зарегистрированное устройство.
            if ((userMask & NotificationChannel.WebPush) != NotificationChannel.None
                && usersWithPush.Contains(request.RecipientUserId))
            {
                effective |= NotificationChannel.WebPush;
            }

            NotificationChannel supported = SupportedByTemplate(request.Template);
            NotificationChannel deliverable = effective & registeredChannels & supported;

            if (deliverable == NotificationChannel.None)
            {
                _logger.LogDebug(
                    "No deliverable channels for {UserId} / {Type} (effective={Effective}, registered={Registered}, supported={Supported})",
                    request.RecipientUserId, request.Type, effective, registeredChannels, supported);
                _metrics.RecordDispatch(request.Type, TimeSpan.Zero, status: "skipped_no_channels");
                continue;
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            string dispatchStatus = "success";
            try
            {
                await ProcessOneAsync(request, deliverable, ct);
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                dispatchStatus = "failed";
                _logger.LogError(ex,
                    "Unhandled error dispatching notification for {UserId} / {Type}",
                    request.RecipientUserId, request.Type);
                throw;
            }
            finally
            {
                _metrics.RecordDispatch(
                    request.Type,
                    Stopwatch.GetElapsedTime(startTimestamp),
                    status: dispatchStatus);
            }
        }
    }

    private async Task ProcessOneAsync(
        NotificationRequest request,
        NotificationChannel channels,
        CancellationToken ct)
    {
        // Генерируем id заранее, чтобы inject'ить {openUrl} в args перед рендером:
        // email/telegram templates используют {openUrl}, который резолвится proxy-endpoint'ом
        // (GET /n/{id} → mark-read + 302 на target URL).
        NotificationId id = NotificationId.Create();
        string openUrl = PlatformLinkBuilder.BuildOpenUrl(_options.FrontendBaseUrl, id.Value);
        TemplateArgs enrichedArgs = request.Args.With("openUrl", openUrl);

        // Запекаем готовый targetUrl в payload — единая точка правды URL'ов нотификаций.
        // Route-контекст (authorSlug/courseSlug) готовят handlers до dispatch'а.
        string payloadWithUrl = PayloadWithTargetUrl(request);

        // Inbox хранит InApp-вариант. Telegram/Email рендерятся перед отправкой отдельно.
        RenderedMessage inApp = _renderers.Render(NotificationChannel.InApp, request.Template, enrichedArgs);

        Result<Notification, Error> created = Notification.Create(
            recipientUserId: request.RecipientUserId,
            type: request.Type,
            templateId: request.Template.Id,
            title: inApp.Title,
            body: inApp.Body,
            channels: channels,
            payload: payloadWithUrl,
            correlationId: request.CorrelationId,
            id: id);

        if (created.IsFailure)
        {
            _logger.LogWarning(
                "Failed to build Notification for {UserId} / {Type}: {Error}",
                request.RecipientUserId, request.Type, created.Error.Messages[0].Message);
            throw created.Error.ToException();
        }

        Notification notification = created.Value;

        UnitResult<Error> beginResult = await _transactions.BeginTransactionAsync(ct);
        if (beginResult.IsFailure)
        {
            _logger.LogError(
                "Failed to begin transaction for notification dispatch ({UserId}/{Type}/{CorrelationId}): {Error}",
                request.RecipientUserId, request.Type, request.CorrelationId,
                beginResult.Error.Messages[0].Message);
            throw beginResult.Error.AsTransient().ToException();
        }

        await _notifications.AddAsync(notification, ct);

        string? telegramBody = null;
        // template.Telegram уже не null здесь — channels пересеклись с SupportedByTemplate в DispatchAsync.
        if ((channels & NotificationChannel.Telegram) != NotificationChannel.None
            && request.Template.Telegram is not null)
        {
            RenderedMessage telegram = _renderers.Render(NotificationChannel.Telegram, request.Template, enrichedArgs);
            telegramBody = telegram.Body;
        }

        await _outbox.PublishAsync(new NotificationCreated(
            NotificationId: notification.Id.Value,
            RecipientUserId: notification.RecipientUserId,
            Type: (short)notification.Type,
            Channels: (short)notification.Channels,
            TemplateId: notification.TemplateId,
            Title: notification.Title,
            Body: notification.Body,
            TelegramBody: telegramBody,
            PayloadJson: notification.Payload,
            CorrelationId: notification.CorrelationId,
            CreatedAt: new DateTimeOffset(notification.CreatedAt, TimeSpan.Zero)));

        UnitResult<Error> commit = await _transactions.CommitTransactionAsync(ct);
        if (commit.IsFailure)
        {
            ErrorMessage firstError = commit.Error.Messages[0];

            if (string.Equals(firstError.Code, "notification.already.exists", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Notification already exists for correlation {CorrelationId} / {UserId} / {Type}",
                    request.CorrelationId, request.RecipientUserId, request.Type);
                return;
            }

            _logger.LogError(
                "Failed to commit notification: {Code} / {Message}",
                firstError.Code, firstError.Message);
            throw commit.Error.AsTransient().ToException();
        }

        await DeliverAsync(notification, request, enrichedArgs, inApp, ct);
    }

    private async Task DeliverAsync(
        Notification notification,
        NotificationRequest request,
        TemplateArgs enrichedArgs,
        RenderedMessage inAppCached,
        CancellationToken ct)
    {
        foreach (NotificationChannel channel in EnumerateFlags(notification.Channels))
        {
            if (!_channels.TryGet(channel, out INotificationChannel? impl) || impl is null)
                continue;

            // InApp уже отрендерен выше для БД-записи — переиспользуем, не рендерим повторно.
            // Telegram/Email — рендер per-channel из template parts с обогащёнными args
            // (включают {openUrl} → короткая proxy-ссылка на endpoint mark-read + redirect).
            // WebPush, как и InApp, использует уже отрендеренный InApp-вариант (Title/Body) —
            // у него нет отдельного channel-renderer'а.
            RenderedMessage rendered =
                channel is NotificationChannel.InApp or NotificationChannel.WebPush
                    ? inAppCached
                    : _renderers.Render(channel, request.Template, enrichedArgs);

            DeliveryResult result;
            try
            {
                result = await impl.SendAsync(notification, rendered, ct);
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(
                    ex,
                    "Channel {Channel} threw while sending notification {NotificationId}",
                    channel, notification.Id.Value);
                result = DeliveryResult.Failed("channel.exception", ex.GetType().Name);
            }

            await LogDeliveryAsync(notification.Id, channel, result, ct);
        }
    }

    private async Task LogDeliveryAsync(
        NotificationId notificationId,
        NotificationChannel channel,
        DeliveryResult result,
        CancellationToken ct)
    {
        Result<NotificationDelivery, Error> created = NotificationDelivery.Create(notificationId, channel);
        if (created.IsFailure)
            return;

        NotificationDelivery delivery = created.Value;

        if (result.IsSuccess)
            delivery.MarkDelivered(result.ProviderMessageId);
        else if (result.IsSkipped)
            delivery.MarkSkipped(result.ErrorCode ?? "unknown", result.ErrorDetail);
        else
            delivery.MarkFailed(result.ErrorCode ?? "unknown", result.ErrorDetail ?? string.Empty);

        await _deliveries.AddAsync(delivery, ct);
        UnitResult<Error> saved = await _transactions.SaveChangesAsync(ct);
        if (saved.IsFailure)
            _logger.LogWarning("Failed to persist delivery log for {NotificationId} / {Channel}",
                notificationId.Value, channel);

        string status = result.IsSuccess ? "delivered" : (result.IsSkipped ? "skipped" : "failed");
        _metrics.RecordDelivery(channel, status, result.ErrorCode);
    }

    /// <summary>
    /// Хоть у одного из recipients включён Email и шаблон поддерживает email — нужно
    /// прогреть Auth cache. Иначе prewarm — пустая трата HTTP call'а.
    /// </summary>
    private static bool HasEmailRecipients(
        IReadOnlyList<NotificationRequest> requests,
        IReadOnlyDictionary<Guid, UserNotificationChannels> userChannelsMap)
    {
        foreach (NotificationRequest request in requests)
        {
            if (request.Template.Email is null)
                continue;

            // Email-канал effective: requested + user enabled (или Email форсирован шаблоном —
            // критичное уведомление идёт поверх выключенного Email-канала, #704).
            if ((request.RequestedChannels & NotificationChannel.Email) == NotificationChannel.None)
                continue;

            bool emailForced =
                (request.Template.ForcedChannels & NotificationChannel.Email) != NotificationChannel.None;
            bool emailEnabled = emailForced
                || !userChannelsMap.TryGetValue(request.RecipientUserId, out UserNotificationChannels? row)
                || row.EmailEnabled;
            if (emailEnabled)
                return true;
        }

        return false;
    }

    private static NotificationChannel ResolveUserMask(
        Guid userId,
        IReadOnlyDictionary<Guid, UserNotificationChannels> map)
    {
        NotificationChannel mask = NotificationChannel.InApp; // всегда ON
        if (!map.TryGetValue(userId, out UserNotificationChannels? row))
        {
            // Дефолты для пользователя без записи: Telegram + Email + WebPush ON (см. UserNotificationChannels.Default).
            // TG-доставка без UserLink-а сама дропнется на TelegramBotService — флаг безопасно держать ON.
            // WebPush без зарегистрированного устройства отфильтруется выше (usersWithPush).
            return mask | NotificationChannel.Telegram | NotificationChannel.Email | NotificationChannel.WebPush;
        }

        if (row.TelegramEnabled)
            mask |= NotificationChannel.Telegram;
        if (row.EmailEnabled)
            mask |= NotificationChannel.Email;
        if (row.WebPushEnabled)
            mask |= NotificationChannel.WebPush;
        return mask;
    }

    private NotificationChannel AggregateRegisteredChannels()
    {
        NotificationChannel mask = NotificationChannel.None;
        foreach (NotificationChannel channel in _channels.RegisteredChannels)
            mask |= channel;
        // Telegram доставляется через NotificationCreated-event в TelegramBotService,
        // а не через локально зарегистрированный INotificationChannel. С точки зрения
        // dispatcher'а канал постоянно "registered" — фактическая доставка ограничивается
        // через `supported` (template.Telegram is not null) и user's TelegramEnabled-флаг.
        mask |= NotificationChannel.Telegram;
        return mask;
    }

    /// <summary>
    /// Какие каналы вообще поддерживает шаблон (есть part). Если у шаблона нет
    /// <see cref="Templates.Parts.TelegramTemplate"/>, диспатчить Telegram нельзя — даже если
    /// пользователь его включил и канал зарегистрирован, контента просто нет.
    /// </summary>
    private static NotificationChannel SupportedByTemplate(Templates.NotificationTemplate template)
    {
        NotificationChannel mask = NotificationChannel.InApp;
        if (template.Telegram is not null) mask |= NotificationChannel.Telegram;
        if (template.Email is not null) mask |= NotificationChannel.Email;
        // WebPush переиспользует InApp Title/Body (отдельного part'а нет) — поддержан всегда,
        // раз у шаблона есть обязательный InApp.
        mask |= NotificationChannel.WebPush;
        return mask;
    }

    private static IEnumerable<NotificationChannel> EnumerateFlags(NotificationChannel value)
    {
        if ((value & NotificationChannel.InApp) != NotificationChannel.None)
            yield return NotificationChannel.InApp;
        if ((value & NotificationChannel.Telegram) != NotificationChannel.None)
            yield return NotificationChannel.Telegram;
        if ((value & NotificationChannel.Email) != NotificationChannel.None)
            yield return NotificationChannel.Email;
        if ((value & NotificationChannel.WebPush) != NotificationChannel.None)
            yield return NotificationChannel.WebPush;
    }

    /// <summary>
    /// Запекает <c>targetUrl</c> в payload-JSON: вычисляет финальный URL через
    /// <see cref="PlatformLinkBuilder.BuildTargetUrl"/> по (type + payload) и кладёт
    /// под ключом <c>targetUrl</c>. Если payload пуст или невалиден — возвращает
    /// <c>{"targetUrl":"..."}</c>.
    /// </summary>
    private string PayloadWithTargetUrl(NotificationRequest request)
    {
        string targetUrl = PlatformLinkBuilder.BuildTargetUrl(
            _options.FrontendBaseUrl,
            (short)request.Type,
            request.PayloadJson);

        Dictionary<string, JsonElement> dict = ParsePayloadOrEmpty(request.PayloadJson);
        dict["targetUrl"] = JsonSerializer.SerializeToElement(targetUrl);
        return JsonSerializer.Serialize(dict);
    }

    private static Dictionary<string, JsonElement> ParsePayloadOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "{}", StringComparison.Ordinal))
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }
}
