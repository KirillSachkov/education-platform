using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace NotificationService.Web.Configuration;

/// <summary>
/// Браузерный нативный <c>EventSource</c> не умеет слать кастомные headers, поэтому на
/// SSE-эндпоинт <c>GET /notifications/stream</c> JWT приходит query-параметром
/// <c>access_token</c> — стандартный паттерн ASP.NET Core для SSE / WebSocket / SignalR.
/// Читаем его в <see cref="JwtBearerEvents.OnMessageReceived"/> строго для этого одного
/// пути; все остальные эндпоинты по-прежнему берут токен только из заголовка
/// <c>Authorization</c>. Токен короткоживущий (5 мин) и идёт по HTTPS; nginx не логирует
/// SSE-локацию, так что в access-логи он не попадает.
/// </summary>
internal static class SseQueryStringAuth
{
    private const string StreamPath = "/notifications/stream";
    private const string TokenQueryKey = "access_token";

    public static IServiceCollection AddSseQueryStringTokenAuth(this IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                JwtBearerEvents events = options.Events ??= new JwtBearerEvents();
                Func<MessageReceivedContext, Task> previous = events.OnMessageReceived;

                events.OnMessageReceived = async context =>
                {
                    // Сохраняем любую ранее настроенную логику (на будущее).
                    await previous(context);

                    // Заголовок Authorization уже дал токен — ничего не подменяем.
                    if (!string.IsNullOrEmpty(context.Token))
                    {
                        return;
                    }

                    // Только SSE-стрим читает токен из query — больше нигде.
                    if (!context.Request.Path.StartsWithSegments(
                            StreamPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string token = context.Request.Query[TokenQueryKey].ToString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        context.Token = token;
                    }
                };
            });

        return services;
    }
}
