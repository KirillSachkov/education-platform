using Core.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Core.Channels;
using NotificationService.Core.Channels.Email;
using NotificationService.Core.Channels.InApp;
using NotificationService.Core.Channels.WebPush;
using NotificationService.Core.Diagnostics;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Sse;
using NotificationService.Core.Templates.Rendering;

namespace NotificationService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);
        services.AddHandlers(typeof(Registration).Assembly);

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection("Notifications"))
            .Validate(
                options => Uri.TryCreate(options.FrontendBaseUrl, UriKind.Absolute, out Uri? uri)
                           && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)),
                "Notifications:FrontendBaseUrl must be an absolute HTTP(S) URI.")
            .Validate(
                options => !options.Retention.Enabled
                           || (options.Retention.DefaultDays is >= 1 and <= 3650
                               && options.Retention.IntervalHours is >= 1 and <= 168
                               && options.Retention.BatchSize is >= 1 and <= 50_000
                               && options.Retention.InitialDelaySeconds is >= 0 and <= 86_400),
                "Notifications:Retention values are outside safe bounds.")
            .Validate(
                options => !options.Digest.Enabled
                           || (Enum.IsDefined(options.Digest.DayOfWeekUtc)
                               && options.Digest.HourUtc is >= 0 and <= 23
                               && options.Digest.CheckIntervalMinutes is >= 1 and <= 1440
                               && options.Digest.InitialDelaySeconds is >= 0 and <= 86_400),
                "Notifications:Digest schedule is invalid.")
            .ValidateOnStart();

        services.AddOptions<WebPushOptions>()
            .Bind(configuration.GetSection(WebPushOptions.SECTION))
            .Validate(
                options => string.IsNullOrWhiteSpace(options.PublicKey)
                           == string.IsNullOrWhiteSpace(options.PrivateKey),
                "Notifications:WebPush must provide both PublicKey and PrivateKey, or neither.")
            .Validate(
                options => !options.IsConfigured
                           || (Uri.TryCreate(options.Subject, UriKind.Absolute, out Uri? uri)
                               && (string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))),
                "Notifications:WebPush:Subject must be an absolute mailto: or https: URI.")
            .ValidateOnStart();

        // Бизнес-метрики notification pipeline. Singleton — Meter живёт всё время процесса
        // (см. правило в root CLAUDE.md «точечно через IMeterFactory»).
        services.AddSingleton<NotificationMetrics>();

        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Еженедельный дайджест (#468): runner = один проход (агрегация + dispatch).
        // Фоновое расписание — WeeklyDigestService в Infrastructure.Postgres.
        services.AddScoped<Features.Digest.IWeeklyDigestRunner, Features.Digest.WeeklyDigestRunner>();

        // Кампания «приглашение на тест уровня» (#554): admin-triggered рассылка всем
        // пользователям. Endpoints авто-discover'ятся, runner — DI как и дайджест.

        services.AddScoped<Features.Campaigns.IEmailLoginNoticeCampaignRunner, Features.Campaigns.EmailLoginNoticeCampaignRunner>();
        services.AddScoped<Features.Campaigns.ILinkAccountsNudgeCampaignRunner, Features.Campaigns.LinkAccountsNudgeCampaignRunner>();

        // Channels — InApp, Email. Telegram = отдельный сервис (TelegramBotService),
        // уведомления доставляются через RabbitMQ consumer'ом там.
        services.AddScoped<ChannelRegistry>();
        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();

        // WebPush — регистрируем канал только если заданы VAPID-ключи (Notifications:WebPush).
        // Без ключей сервис работает как раньше (InApp/Email/Telegram), push не доставляется —
        // канал не попадает в ChannelRegistry и dispatcher его не предлагает (issue #342).
        WebPushOptions webPushOptions = new();
        configuration.GetSection(WebPushOptions.SECTION).Bind(webPushOptions);
        if (webPushOptions.IsConfigured)
        {
            services.AddMemoryVapidTokenCache();
            services.AddPushServiceClient(o =>
            {
                o.Subject = webPushOptions.Subject;
                o.PublicKey = webPushOptions.PublicKey;
                o.PrivateKey = webPushOptions.PrivateKey;
            });
            services.AddScoped<IWebPushSender, LibNetWebPushSender>();
            services.AddScoped<INotificationChannel, WebPushNotificationChannel>();
        }

        // Channel renderers — один на канал. Telegram-рендер нужен для NotificationCreated event'а,
        // который читает TelegramBotService (он не знает про Core шаблоны), поэтому
        // регистрируем все три (включая Telegram) здесь.
        services.AddSingleton<ChannelRendererRegistry>();
        services.AddSingleton<IChannelRenderer, InAppRenderer>();
        services.AddSingleton<IChannelRenderer, TelegramRenderer>();
        services.AddSingleton<IChannelRenderer, EmailRenderer>();

        // SSE hub: заглушка по умолчанию. Реальный hub перерегистрируется в .Web.
        services.TryAddSingleton<ISseConnectionHub, NullSseConnectionHub>();

        return services;
    }
}