using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramBotService.Core.Diagnostics;
using TelegramBotService.Core.Features.ChatMember.Handlers;
using TelegramBotService.Core.Features.ChatMember.Middleware;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Core.Features.Onboarding;
using TelegramBotService.Core.Features.MainMenu.Services;
using TelegramBotService.Core.Features.UserLinks.Handlers;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Core.Options;

namespace TelegramBotService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramNotificationOptions>()
            .Bind(configuration.GetSection(TelegramNotificationOptions.SECTION_NAME))
            .Validate(
                options => string.IsNullOrWhiteSpace(options.FrontendBaseUrl)
                    || (Uri.TryCreate(options.FrontendBaseUrl, UriKind.Absolute, out Uri? uri)
                        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))),
                "TelegramNotification:FrontendBaseUrl must be an absolute HTTP(S) URL.")
            .ValidateOnStart();

        // OTel метрики: send latency, throttle wait, handler lag, outcome counter.
        // Singleton — Meter живёт всё время процесса (root CLAUDE.md правило).
        services.AddSingleton<TelegramMetrics>();

        services.AddScoped<LinkAccountHandler>();
        services.AddScoped<UnlinkHandler>();

        // Chat-id discovery (auto-message при promotion + /chatid команда из группы).
        services.AddScoped<MyChatMemberHandler>();
        services.AddScoped<ChatIdQueryMiddleware>();

        // Course-chat binding feature.
        // IChatAdministrationApi регистрируется TBF'ом в AddTelegramBotFlow (singleton).
        services.AddScoped<BindChatHandler>();
        services.AddScoped<UnbindChatHandler>();
        services.AddScoped<ListChatBindingsHandler>();
        services.AddScoped<HasActiveChatBindingHandler>();
        services.AddScoped<CheckPlanMembershipHandler>();
        services.AddScoped<UpdateChatBindingFlagsHandler>();
        services.AddScoped<GetMyChatsHandler>();
        services.AddScoped<ResyncMyInvitesHandler>();

        // Support/admin actions (issue #444): per-user TG link, plan chats, welcome resend,
        // admin resync-invites. Endpoint'ы auto-discovered; handler'ы — scoped DI.
        services.AddScoped<GetAdminUserTelegramLinkHandler>();
        services.AddScoped<GetAdminPlanChatsHandler>();
        services.AddScoped<ResendPlanWelcomeHandler>();
        services.AddScoped<ResyncUserInvitesHandler>();
        services.AddScoped<TelegramInviteResyncService>();
        services.AddScoped<ChatMembershipChecker>();
        services.AddScoped<PlanWelcomeService>();
        services.AddScoped<ChatJoinRequestHandler>();
        services.AddScoped<ChatMemberUpdateHandler>();
        services.AddScoped<ClaimCourseAccessHandler>();
        services.AddScoped<PlanGrantCreatedTelegramHandler>();
        services.AddScoped<PlanGrantRevokedTelegramHandler>();
        services.AddScoped<UserTelegramLinkedHandler>();

        // Onboarding feature — frontend wizard поллит /telegram/me/onboarding-status/
        // во время TELEGRAM step.
        services.AddScoped<GetMyPlanOnboardingStatusHandler>();

        services.AddScoped<ChatBindingHealthService>();
        services.AddScoped<ClaimAnnouncementService>();
        services.AddHostedService<ChatBindingHealthCheckService>();

        // Singleton — собственный scope per call, не зависит от ambient request scope.
        services.AddSingleton<IBotDecisionLogger, BotDecisionLogger>();
        services.AddHostedService<BotDecisionRetentionService>();

        // Startup-задача: сбрасывает menu button на slash-команды.
        services.AddHostedService<BotMenuButtonInitializer>();

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);

        // HTTP-эндпоинты use case'ов (admin-bind-chat и т.п.) — auto-discovered via SachkovTech.Framework.
        services.AddEndpoints(typeof(Registration).Assembly);

        return services;
    }
}
