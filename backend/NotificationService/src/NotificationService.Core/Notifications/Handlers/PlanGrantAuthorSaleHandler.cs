using System.Globalization;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / plan_grant.created</c> → уведомление АВТОРУ плана о новом участнике (#428).
/// Sibling к <see cref="PlanGrantReceivedHandler"/> (тот уведомляет получателя гранта) на той же
/// очереди <c>notifications.access.grant_events</c> — Wolverine разводит <see cref="PlanGrantCreated"/>
/// по всем хендлерам. Идемпотентность не страдает: unique-index
/// <c>(correlation_id, recipient_user_id, type)</c> различает recipient (автор vs получатель) и type.
///
/// Автор видит, КТО получил доступ (имя + платформенный ник + email), К КАКОМУ плану (название —
/// <c>PlanName</c> из события, #445), КАКОЙ объём доступа (tier) и НА КАКОЙ СРОК — «Навсегда» vs
/// «На месяц» (#632, из <c>ExpiresAt</c>/<c>GrantedAt</c>; оба плана — FULL_ALL, tier их не различает).
/// «План/курс», «Доступ» и «Срок» — разные сущности, разнесены в тексте, чтобы автор их не путал.
/// Данные покупателя обогащаются через <see cref="IAuthServiceClient"/>; при ошибке enrichment —
/// graceful fallback ("Пользователь" / "—"), уведомление всё равно доходит. Имя плана при пустом
/// <c>PlanName</c> — fallback «не указан».
///
/// Уведомляет только о подтверждённой покупке:
/// <list type="bullet">
///   <item><c>Source=PURCHASE</c> — grant создан после успешного payment webhook.</item>
///   <item>Остальные источники (GitHub, Telegram, trial, invite, admin, migration) не доказывают
///   оплату и не создают author-sale.</item>
///   <item>Self-grant (автор плана == получатель) — автор не уведомляет сам себя.</item>
/// </list>
/// </summary>
public sealed class PlanGrantAuthorSaleHandler
{
    private const string PURCHASE_SOURCE = "PURCHASE";

    private readonly INotificationDispatcher _dispatcher;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<PlanGrantAuthorSaleHandler> _logger;

    public PlanGrantAuthorSaleHandler(
        INotificationDispatcher dispatcher,
        IAuthServiceClient authClient,
        ILogger<PlanGrantAuthorSaleHandler> logger)
    {
        _dispatcher = dispatcher;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(PlanGrantCreated evt, CancellationToken ct)
    {
        // Author-sale означает реальную оплату, а не бесплатную/ручную выдачу доступа.
        if (!string.Equals(evt.Source, PURCHASE_SOURCE, StringComparison.Ordinal))
            return;

        // Автор не уведомляет сам себя (например, тестовая покупка на собственном аккаунте).
        if (evt.PlanAuthorId == Guid.Empty || evt.PlanAuthorId == evt.UserId)
            return;

        (string buyerLine, string buyerName, string? buyerUsername, string buyerEmail) =
            await ResolveBuyerAsync(evt.UserId, ct);

        string planName = string.IsNullOrWhiteSpace(evt.PlanName) ? "не указан" : evt.PlanName;
        string accessSummary = BuildSummary(evt.PlanTier);
        // #632: срок доступа — «Навсегда» vs «На месяц». Автору важно сразу видеть, купили
        // пожизненный доступ или пробный месячный (оба плана — FULL_ALL, tier их не различает).
        string accessTerm = BuildTermLabel(evt.GrantedAt, evt.ExpiresAt);
        string sourceLabel = MapSource(evt.Source);

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.PlanGrantAuthorSale,
            recipientUserId: evt.PlanAuthorId,
            correlationId: evt.GrantId,
            args: TemplateArgs.Of(
                ("buyerLine", buyerLine),
                ("buyerEmail", buyerEmail),
                ("planName", planName),
                ("accessSummary", accessSummary),
                ("accessTerm", accessTerm),
                ("sourceLabel", sourceLabel)),
            payload: new
            {
                grantId = evt.GrantId,
                planId = evt.PlanId,
                planName,
                planTier = evt.PlanTier,
                accessTerm,
                isLifetime = evt.ExpiresAt is null,
                expiresAt = evt.ExpiresAt,
                source = evt.Source,
                buyerUserId = evt.UserId,
                buyerName,
                buyerUsername,
                buyerEmail,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<(string Line, string Name, string? Username, string Email)> ResolveBuyerAsync(
        Guid userId, CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
            await _authClient.GetUsersByIdsAsync([userId], ct);

        if (lookup.IsSuccess && lookup.Value is { Count: > 0 })
        {
            AuthUserLookupDto user = lookup.Value[0];
            // Не используем @-префикс для платформенного username: в Telegram @name — это
            // mention-ссылка на любого TG-юзера с таким ником, не связанного с покупателем.
            string name = user.Name ?? user.Username ?? "Пользователь";
            string line = user.Name is not null && user.Username is not null
                ? $"{user.Name} ({user.Username})"
                : name;
            string email = string.IsNullOrWhiteSpace(user.Email) ? "—" : user.Email;
            return (line, name, user.Username, email);
        }

        _logger.LogWarning(
            "Auth lookup failed for plan-grant buyer {UserId}: {Error}. Using fallback.",
            userId, lookup.ErrorText());
        return ("Пользователь", "Пользователь", null, "—");
    }

    // Объём доступа в виде краткой noun-phrase — читается под лейблом «Доступ:» без тавтологии.
    private static string BuildSummary(string planTier) => planTier switch
    {
        PlanTierNames.FULL_ALL => "направление .NET Fullstack",
        PlanTierNames.LEARN_ALL => "материалы .NET Fullstack",
        PlanTierNames.COURSE => "курс",
        PlanTierNames.SUBSCRIPTION => "подписка",
        _ => "учебные материалы",
    };

    // Срок доступа для автора (#632): «Навсегда» (бессрочный грант, ExpiresAt=null) или
    // «На месяц · до DD.MM.YYYY» для time-bounded гранта (пробный месяц #580 и пр.). Сигнал —
    // ExpiresAt/GrantedAt самого события, без обратного вызова в AccessService.
    private static string BuildTermLabel(DateTimeOffset grantedAt, DateTimeOffset? expiresAt)
    {
        if (expiresAt is not { } expiry)
            return "Навсегда";

        int days = Math.Max(1, (int)Math.Round((expiry - grantedAt).TotalDays));
        // 28–31 дн. → «На месяц» (пробный месяц #580). Прочие сроки (напр. будущий
        // 14-дневный грант) дают «На N дн. · до DD.MM.YYYY» — расширить bucket-список,
        // если в AccessService появятся новые стандартные длительности.
        string duration = days is >= 28 and <= 31
            ? "На месяц"
            : string.Create(CultureInfo.InvariantCulture, $"На {days} дн.");
        return string.Create(CultureInfo.InvariantCulture, $"{duration} · до {expiry:dd.MM.yyyy}");
    }

    private static string MapSource(string source) => source switch
    {
        "PURCHASE" => "Покупка",
        "INVITE_LINK" => "Инвайт-ссылка",
        "ADMIN_GRANT" => "Выдан вручную",
        "GITHUB_ORG" => "GitHub-организация",
        "TRIAL" => "Бесплатный план",
        "TELEGRAM_F1" => "Telegram",
        _ => source,
    };
}
