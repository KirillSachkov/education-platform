using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Exceptions;
using PlatformAuth.Authorization;

namespace Observability;

/// <summary>
/// Extension methods for configuring OpenTelemetry observability
/// (traces + metrics). Логи — отдельный pipeline: Serilog Console (stdout) →
/// Alloy (Docker socket) → Loki. OTLP-канал для логов намеренно не используем
/// (см. AddSerilogLogging).
///
/// OTLP endpoint: env var <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>. Если не задан —
/// <c>.AddOtlpExporter()</c> не регистрируется (иначе SDK по дефолту ломится
/// в localhost:4317 и спамит retry-ями фоном).
///
/// Sampling ratio: <c>Observability:SamplingRatio</c> в appsettings (1.0 = 100%).
/// Дефолт: 1.0 в Development/Docker, 0.1 в Production. Tail-sampling в prod
/// (errors+slow+10%) делается на уровне otel-collector.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Имя ActivitySource + Meter для Shared ContentAccess подсистемы.
    /// Жёстко захардкожен, чтобы Observability не тянул project-reference на ContentAccess.
    /// </summary>
    private const string CONTENT_ACCESS_SOURCE = "ContentAccess";

    /// <summary>Wolverine ActivitySource + Meter — message handlers, outbox dispatch, durability metrics.</summary>
    private const string WOLVERINE_SOURCE = "Wolverine";

    /// <summary>Npgsql ActivitySource — низкоуровневые SQL-запросы (Dapper тоже виден).</summary>
    private const string NPGSQL_SOURCE = "Npgsql";

    /// <summary>Notification pipeline meter — dispatch latency, channel deliveries, outbox lag.</summary>
    public const string NOTIFICATIONS_METER = "EducationPlatform.Notifications";

    /// <summary>Telegram delivery meter — send latency, throttle wait, outcome counters.</summary>
    public const string TELEGRAM_METER = "EducationPlatform.Telegram";

    /// <summary>Plan onboarding meter — invitation outcomes, webhook signature failures, GitHub API latency.</summary>
    public const string ONBOARDING_METER = "EducationPlatform.Onboarding";

    /// <summary>AI usage meter — input/output tokens, request latency, errors per model+use_case+service.
    /// Регистрируется в OTel pipeline через `AddMeter(AI_METER)`. См. Shared/AI/MeteredAiClient.</summary>
    public const string AI_METER = "EducationPlatform.AI";

    /// <summary>Payments meter — order lifecycle counters, provider Init/webhook outcomes, latency histograms.</summary>
    public const string PAYMENTS_METER = "EducationPlatform.Payments";

    /// <summary>
    ///     AssignmentReviewService meter — iteration durations, verdict distribution,
    ///     diff-size histogram, RAG context-chunk count, GitHub API latency, AI cost.
    ///     ActivitySource имя совпадает (одно слово, переиспользуется).
    /// </summary>
    public const string ASSIGNMENT_REVIEW_SOURCE = "EducationPlatform.AssignmentReview";

    public static IConfigurationBuilder AddSerilogConfiguration(
        this IConfigurationBuilder configuration,
        string environmentName)
    {
        string baseDir = AppContext.BaseDirectory;

        configuration
            .AddJsonFile(Path.Combine(baseDir, "serilog.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(baseDir, $"serilog.{environmentName}.json"),
                optional: true,
                reloadOnChange: false);

        return configuration;
    }

    public static IServiceCollection AddSerilogLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        string serviceVersion = ResolveServiceVersion();
        string environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        // Логи пишем ТОЛЬКО в Console (sinks конфигурятся через serilog.{Env}.json).
        // Сбор и доставка в Loki — задача Alloy: он читает stdout Docker-контейнеров,
        // парсит JSON (level/trace_id/etc.) и пушит в Loki напрямую. OTLP-канал для логов
        // намеренно не используем — теряем nginx/postgres/redis stdout, и при крэше app
        // OTLP buffer не успевает flush'нуться, а stdout гарантированно сохраняется.
        services.AddSerilog((sp, lc) =>
        {
            lc.ReadFrom.Configuration(configuration)
                .ReadFrom.Services(sp)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty("ServiceName", serviceName)
                .Enrich.WithProperty("ServiceVersion", serviceVersion)
                .Enrich.WithProperty("DeploymentEnvironment", environmentName);
        });

        return services;
    }

    /// <summary>
    /// Минимальный OTel pipeline для метрик-only — без traces и Serilog.
    /// Используется TelegramBotService, у которого TBF собственная Serilog-инициализация
    /// + ActivitySource'ы фреймворка несовместимы с нашим tracing pipeline'ом
    /// (`PlatformOptions.EnableObservability=false`). Но метрики ASP.NET/Runtime/HttpClient
    /// + кастомный <c>EducationPlatform.Telegram</c> meter — нужны для notification-pipeline
    /// dashboard (issue #67). Регистрирует только <c>WithMetrics</c>, OTLP exporter
    /// активируется только если задан <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
    /// </summary>
    public static IServiceCollection AddObservabilityMetricsOnly(
        this IServiceCollection services,
        string serviceName)
    {
        bool otlpEnabled = IsOtlpEnabled();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => ConfigurePlatformResource(resource, serviceName))
            .WithMetrics(metrics =>
            {
                ConfigurePlatformMeters(metrics);

                if (otlpEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        return services;
    }

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        string environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        // Sampling ratio: priority order
        // 1. appsettings: Observability:SamplingRatio
        // 2. env var: OTEL_TRACES_SAMPLER_ARG
        // 3. default: 1.0 dev/docker, 0.1 production
        double samplingRatio = ResolveSamplingRatio(configuration, environmentName);

        // Если OTLP endpoint не задан — не регистрируем OTLP exporter, иначе SDK по дефолту
        // ломится в http://localhost:4317 и фоном спамит retry-ями, когда obs-стэк не поднят.
        bool otlpEnabled = IsOtlpEnabled();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => ConfigurePlatformResource(resource, serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Health/metrics → не трейсить (мусор + дублирование).
                        // Liveness/readiness тоже отбрасываем.
                        // /notifications/stream → SSE с JWT в query-параметре access_token
                        // (#457, нативный EventSource не шлёт headers). Не трейсим: иначе токен
                        // утечёт в url.query спана (Tempo, retention 24h). Длинный SSE-span и
                        // так малополезен для трейсинга.
                        options.Filter = httpContext =>
                        {
                            string path = httpContext.Request.Path.Value ?? string.Empty;
                            return !path.StartsWith("/health", StringComparison.Ordinal) &&
                                   !path.StartsWith("/metrics", StringComparison.Ordinal) &&
                                   !path.StartsWith("/notifications/stream", StringComparison.Ordinal);
                        };
                        options.RecordException = true;
                        // client_ip намеренно не пишем — это PII (GDPR).
                        // Если понадобится — хешировать или обрезать до /24 подсети.
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.FilterHttpRequestMessage = req =>
                            !req.RequestUri?.AbsolutePath.EndsWith("/health", StringComparison.Ordinal) ?? true;
                    })
                    .AddEntityFrameworkCoreInstrumentation()
                    // ContentAccess — кастомные spans вокруг entitlement-проверок.
                    .AddSource(CONTENT_ACCESS_SOURCE)
                    .AddSource(ASSIGNMENT_REVIEW_SOURCE)
                    // SearchService — spans вокруг Typesense-запросов.
                    .AddSource("SearchService")
                    .AddSource(WOLVERINE_SOURCE)
                    .AddSource(NPGSQL_SOURCE)
                    // RabbitMQ.Client (transitive через Wolverine.RabbitMQ) шлёт W3C
                    // trace context автоматически — нужно только подписаться.
                    .AddSource("RabbitMQ.Client.*");

                if (otlpEnabled)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                ConfigurePlatformMeters(metrics);

                if (otlpEnabled)
                {
                    metrics.AddOtlpExporter();
                }
            });

        // Late-bound Redis instrumentation отключён: AddRedisInstrumentation через
        // ConfigureOpenTelemetryTracerProvider пытается вызвать TracerProviderBuilderSdk
        // .ConfigureServices уже после того, как ServiceProvider собран — бросает
        // NotSupportedException("Services cannot be configured after ServiceProvider
        // has been created") и роняет startup. Это известный баг OTel Redis-пакета
        // при late-bound регистрации. Включим обратно, когда переедем на AddOpenTelemetry
        // .WithTracing(...).AddRedisInstrumentation() через resolver вместо instance.

        return services;
    }

    /// <summary>
    /// Маппит /health/live (всегда 200, процесс жив) и /health/ready (полный набор checks).
    /// /health сохраняется как alias для /health/ready для обратной совместимости.
    ///
    /// Docker/k8s healthcheck должен ходить на /health/live — иначе при просадке Redis/PG
    /// все контейнеры каскадно рестартуют и платформа умирает мгновенно.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: процесс отвечает = жив. Без зависимостей.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false, // ни одну проверку не вызываем
        }).AllowAnonymousEndpoint();

        // Readiness: все зарегистрированные checks (Redis/PG/RMQ/etc).
        app.MapHealthChecks("/health/ready").AllowAnonymousEndpoint();

        // Backward compat: /health = /health/ready.
        app.MapHealthChecks("/health").AllowAnonymousEndpoint();

        return app;
    }

    public static IApplicationBuilder UsePlatformRequestLogging(this IApplicationBuilder app)
    {
        app.UseMiddleware<UserContextLogEnricher>();
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex is not null || httpContext.Response.StatusCode >= 500)
                    return Serilog.Events.LogEventLevel.Error;
                if (httpContext.Response.StatusCode >= 400)
                    return Serilog.Events.LogEventLevel.Warning;
                if (elapsed > 1000)
                    return Serilog.Events.LogEventLevel.Warning;
                return Serilog.Events.LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diag, ctx) =>
            {
                diag.Set("RequestHost", ctx.Request.Host.Value);
                diag.Set("RequestScheme", ctx.Request.Scheme);
                diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
                if (ctx.Request.RouteValues.TryGetValue("controller", out var controller))
                    diag.Set("Controller", controller);
            };
        });

        return app;
    }

    /// <summary>
    /// Унифицированная resource-секция (service.name + version + environment + host) для
    /// всех OTel pipeline'ов платформы. Использование ConfigureResource гарантирует,
    /// что ровно один блок атрибутов уйдёт во все три signals (traces, metrics, logs).
    /// </summary>
    private static void ConfigurePlatformResource(ResourceBuilder resource, string serviceName)
    {
        string serviceVersion = ResolveServiceVersion();
        string environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        resource
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", environmentName),
                new KeyValuePair<string, object>("host.name", Environment.MachineName)
            ]);
    }

    /// <summary>
    /// Платформенный список AddMeter + AddView (histogram boundaries) — единый для
    /// AddObservability и AddObservabilityMetricsOnly. Без регистрации имени Meter
    /// OTel pipeline молча дропает инструмент.
    ///
    /// Wolverine 5.x экспонирует built-in OTel метрики (outbox/inbox depth, handler
    /// duration, retries, DLQ) через Meter с именем
    /// <c>Wolverine:{ApplicationAssemblyName}</c> — например <c>Wolverine:NotificationService.Core</c>.
    /// Просто <c>.AddMeter("Wolverine")</c> НЕ работает — это другое имя.
    /// </summary>
    private static void ConfigurePlatformMeters(MeterProviderBuilder metrics)
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            // ASP.NET Core 9+ объединил multiple legacy meters в один корневой.
            // Старые имена тоже живы для обратной совместимости.
            .AddMeter("Microsoft.AspNetCore")
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("Microsoft.AspNetCore.Routing")
            .AddMeter("Microsoft.AspNetCore.Http.Connections")
            .AddMeter("Microsoft.AspNetCore.RateLimiting")
            .AddMeter("System.Net.Http")
            .AddMeter("System.Net.NameResolution")
            // Wolverine — все известные суффиксы платформы (см. xml-doc метода).
            .AddMeter(WOLVERINE_SOURCE)
            .AddMeter("Wolverine:AccessService.Core")
            .AddMeter("Wolverine:AssignmentReviewService.Core")
            .AddMeter("Wolverine:AuthService.Core")
            .AddMeter("Wolverine:CommentService.Core")
            .AddMeter("Wolverine:EducationContentService.Core")
            .AddMeter("Wolverine:FileService.Core")
            .AddMeter("Wolverine:NotificationService.Core")
            .AddMeter("Wolverine:ProgressService.Core")
            .AddMeter("Wolverine:SearchService.Core")
            .AddMeter("Wolverine:TagService.Core")
            .AddMeter("Wolverine:TelegramBotService.Core")
            // Платформенные бизнес-метрики. Сами Meter'ы создаются через IMeterFactory
            // в Diagnostics-классах сервиса (см. NotificationMetrics, TelegramMetrics,
            // OnboardingMetrics в AccessService.Core).
            // Регистрируются здесь ГЛОБАЛЬНО, чтобы любой сервис, использующий full
            // AddObservability, экспортировал бы их если он хост этого Meter'а. Сервисы,
            // не создающие Meter, просто игнорируют регистрацию (no-op для OTel pipeline).
            .AddMeter(NOTIFICATIONS_METER)
            .AddMeter(TELEGRAM_METER)
            .AddMeter(ONBOARDING_METER)
            .AddMeter(AI_METER)
            .AddMeter(PAYMENTS_METER)
            .AddMeter(ASSIGNMENT_REVIEW_SOURCE)
            // SLO-friendly bucket boundaries (секунды) — чтобы p50/p95/p99 ловили
            // как быстрые операции (5-100ms), так и медленные (1-10s) без перекоса.
            .AddView(instrument =>
                instrument is Histogram<double> or Histogram<long>
                    ? new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 10]
                    }
                    : null);
    }

    private static bool IsOtlpEnabled() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    private static string ResolveServiceVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "0.0.0";
    }

    private static double ResolveSamplingRatio(IConfiguration configuration, string environmentName)
    {
        // 1. Конфиг
        string? configured = configuration["Observability:SamplingRatio"];
        if (!string.IsNullOrEmpty(configured) &&
            double.TryParse(configured, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) &&
            parsed >= 0 && parsed <= 1)
        {
            return parsed;
        }

        // 2. Env var (OTEL convention)
        string? envArg = Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG");
        if (!string.IsNullOrEmpty(envArg) &&
            double.TryParse(envArg, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double envParsed) &&
            envParsed >= 0 && envParsed <= 1)
        {
            return envParsed;
        }

        // 3. Default by env
        return environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
            ? 0.1
            : 1.0;
    }
}