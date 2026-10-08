using Framework.Endpoints;
using NotificationService.Core;
using NotificationService.Web.Sse;
using PlatformAuth.Middleware;

namespace NotificationService.Web.Features.Stream;

/// <summary>
/// <c>GET /notifications/stream</c> — Server-Sent Events endpoint. Держит соединение открытым,
/// пушит фреймы вида <c>event: notification.created\ndata: {...}\n\n</c>. Heartbeat каждые 30s —
/// через <see cref="SseHeartbeatService"/>. Закрытие: клиент отключился (<see cref="HttpContext.RequestAborted"/>).
/// </summary>
public sealed class StreamEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/stream", HandleAsync)
            .RequireAuthorization()
            .RequireRateLimiting(NotificationRateLimitPolicies.STREAM);
    }

    private static async Task HandleAsync(
        HttpContext context,
        SseConnectionHub hub,
        UserScopedData user)
    {
        if (!user.IsAuthenticated || user.UserId == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.Headers.Connection = "keep-alive";

        CancellationToken ct = context.RequestAborted;

        // Начальная retry-директива для EventSource.reconnect (15s).
        await context.Response.WriteAsync("retry: 15000\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

        await using SseConnection connection = new(user.UserId);
        hub.Register(connection);

        try
        {
            await foreach (string frame in connection.Reader.ReadAllAsync(ct))
            {
                await context.Response.WriteAsync(frame, ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Клиент отключился — нормальный путь завершения SSE.
        }
        finally
        {
            hub.Unregister(connection);
        }
    }
}
