using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using PlatformAuth.Middleware;
using Serilog.Context;

namespace Observability;

public sealed class UserContextLogEnricher(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserScopedData userScopedData)
    {
        var activity = Activity.Current;
        var disposables = new List<IDisposable>(capacity: 4);

        try
        {
            if (userScopedData.IsAuthenticated)
            {
                disposables.Add(LogContext.PushProperty("UserId", userScopedData.UserId));
            }

            // TraceId/SpanId уже добавляет Serilog.Enrichers.Span, но дублируем
            // в LogContext под нативными OTEL-именами для устойчивой корреляции в Loki.
            if (activity is not null)
            {
                disposables.Add(LogContext.PushProperty("trace_id", activity.TraceId.ToString()));
                disposables.Add(LogContext.PushProperty("span_id", activity.SpanId.ToString()));

                // Прокинем traceparent в response header чтобы фронт мог сцепить
                // браузерный трейс с бэкендовым.
                context.Response.OnStarting(() =>
                {
                    if (!context.Response.Headers.ContainsKey("traceparent"))
                    {
                        context.Response.Headers["traceparent"] =
                            $"00-{activity.TraceId}-{activity.SpanId}-01";
                    }
                    return Task.CompletedTask;
                });
            }

            string? requestId = context.TraceIdentifier;
            if (!string.IsNullOrEmpty(requestId))
            {
                disposables.Add(LogContext.PushProperty("request_id", requestId));
            }

            await next(context);
        }
        finally
        {
            foreach (var d in disposables)
                d.Dispose();
        }
    }
}
