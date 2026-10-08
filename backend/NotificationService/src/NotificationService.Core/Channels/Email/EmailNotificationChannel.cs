using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Templates;
using Shared.Email;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Core.Channels.Email;

/// <summary>
/// Канал доставки уведомлений по email / Email delivery channel.
///
/// Получает email получателя через <see cref="IAuthServiceClient"/> и отправляет HTML-письмо
/// через <see cref="IEmailSender"/>. Тело письма (HTML) и plain-text рендерятся
/// <c>EmailRenderer</c>'ом через встроенный layout; subject — <see cref="RenderedMessage.Title"/>.
/// </summary>
public sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly IEmailSender _sender;
    private readonly IAuthServiceClient _authClient;
    private readonly IDigestEmailDedupStore _dedup;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(
        IEmailSender sender,
        IAuthServiceClient authClient,
        IDigestEmailDedupStore dedup,
        ILogger<EmailNotificationChannel> logger)
    {
        _sender = sender;
        _authClient = authClient;
        _dedup = dedup;
        _logger = logger;
    }

    public NotificationChannel Type => NotificationChannel.Email;

    public async Task<DeliveryResult> SendAsync(
        Notification notification,
        RenderedMessage message,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> users = await _authClient.GetUsersByIdsAsync(
            [notification.RecipientUserId],
            cancellationToken);

        if (users.IsFailure || users.Value.Count == 0)
        {
            _logger.LogWarning(
                "Не удалось получить email для {UserId}: {Error}",
                notification.RecipientUserId,
                users.IsFailure ? users.Error.Messages[0].Message : "пользователь не найден");
            return DeliveryResult.Skipped("user.email.not_found");
        }

        AuthUserLookupDto user = users.Value[0];
        if (string.IsNullOrWhiteSpace(user.Email))
            return DeliveryResult.Skipped("user.email.empty");

        // Дедуп доставки по физическому инбоксу для глобального дайджеста: несколько Identity-
        // аккаунтов могут указывать на один Gmail-инбокс (точки/+tag/регистр RequireUniqueEmail
        // не сворачивает). InApp-запись на каждый user_id уже создана — гасим только повторный
        // email. Ключ — (correlation прохода, sha256 канонического инбокса); claim атомарный:
        // первый аккаунт инбокса в проходе шлёт письмо, остальные → Skipped.
        if (notification.Type == NotificationType.WeeklyDigest
            && notification.CorrelationId is { } digestCorrelation)
        {
            byte[] inboxHash = EmailInboxCanonicalizer.InboxHash(user.Email);
            bool claimed = await _dedup.TryClaimInboxAsync(digestCorrelation, inboxHash, cancellationToken);
            if (!claimed)
            {
                _logger.LogDebug(
                    "Weekly digest email skipped for {UserId}: inbox already received this digest pass (correlation {CorrelationId})",
                    notification.RecipientUserId, digestCorrelation);
                return DeliveryResult.Skipped("digest.duplicate_inbox");
            }
        }

        string toName = user.Name ?? user.Username ?? user.Email;

        // Network/SMTP/HTTP errors → Failed с конкретным error_code (smtp_5xx, unisender_4xx,
        // timeout и т.п.) от sender'а. Durable retry для failed delivery пока не реализован
        // (issue #762); Unisender webhook только фиксирует последующие provider statuses.
        EmailSendResult result = await _sender.SendAsync(
            toEmail: user.Email,
            toName: toName,
            subject: message.Title,
            htmlBody: message.Body,
            textBody: message.PlainTextBody ?? message.Body,
            // Уведомления/digest — обычная рассылка: список отписавшихся уважается (38-ФЗ).
            // Транзакционный bypass — OTP/сброс пароля в AuthEmailSender + EmailLoginNotice
            // (#704): критичное уведомление об аккаунте («GitHub-вход отключён законом — вот
            // как теперь войти»), не реклама по 38-ФЗ; должно дойти и до отписавшихся
            // на стороне провайдера.
            transactional: notification.Type == NotificationType.EmailLoginNotice,
            ct: cancellationToken);

        if (result.IsSuccess)
            return DeliveryResult.Success();

        _logger.LogWarning(
            "Email send failed for {UserId} / notification {NotificationId}: {Code} / {Detail}",
            notification.RecipientUserId, notification.Id.Value,
            result.ErrorCode, result.ErrorDetail);

        return DeliveryResult.Failed(result.ErrorCode ?? "unknown", result.ErrorDetail ?? string.Empty);
    }
}
