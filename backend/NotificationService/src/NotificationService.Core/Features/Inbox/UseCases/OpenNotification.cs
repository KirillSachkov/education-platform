using Core.Database;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Navigation;
using SharedKernel;

namespace NotificationService.Core.Features.Inbox.UseCases;

/// <summary>
/// Proxy endpoint — клик-ссылки из email/telegram/push идут сюда.
/// Flow: mark-as-read → 302 redirect на <c>payload.targetUrl</c>. Если payload битый или URL не
/// запечён, builder fail-closed редиректит на корень.
///
/// Dual-scheme auth (JWT + Identity cookie) — если юзер открыл email на сайте, где уже
/// залогинен, проходит через cookie; если из fresh tab — через JWT. Если auth отсутствует,
/// редиректим на target anyway (mark-as-read тихо пропускаем) — UX важнее strict security,
/// т.к. target URL сам проверит entitlement.
/// </summary>
public sealed class OpenNotificationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Короткий URL-алиас /n/{id} — единственный внешний click-through путь.
        // Все email/Telegram templates используют {openUrl} → PlatformLinkBuilder.BuildOpenUrl → "/n/{id}".
        // Nginx имеет location ^~ /n/ (выше catch-all /) для проксирования в notification-service:8006.
        app.MapGet("/n/{id:guid}", HandleAsync)
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(NotificationRateLimitPolicies.OPEN);
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> HandleAsync(
        [FromRoute] Guid id,
        [FromServices] INotificationsRepository notifications,
        [FromServices] ITransactionManager transactions,
        [FromServices] IOptions<NotificationOptions> options,
        [FromServices] UserScopedData user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        string baseUrl = options.Value.FrontendBaseUrl;
        NotificationId notificationId = NotificationId.Of(id);

        Result<Notification, Error> lookup = await notifications.GetBy(
            x => x.Id == notificationId, ct);

        if (lookup.IsFailure)
        {
            // Notification не найдена — редирект на корень, без ошибки (может быть удалена retention-cleanup).
            return Results.Redirect(baseUrl.TrimEnd('/') + "/");
        }

        Notification notification = lookup.Value;

        // Mark-as-read: только если JWT/Identity auth идентифицировал recipient'а.
        // Иначе тихо пропускаем — target URL может сам require auth (через 401 на фронте).
        if (user.UserId == notification.RecipientUserId && notification.MarkAsRead())
        {
            UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
            // save.IsFailure: не ломаем click-flow — redirect'им в любом случае.
            _ = save;
        }

        string targetUrl = PlatformLinkBuilder.ResolveTargetUrl(
            options.Value.FrontendBaseUrl,
            (short)notification.Type,
            notification.Payload);
        return Results.Redirect(targetUrl);
    }
}
