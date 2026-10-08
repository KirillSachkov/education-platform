using Core.Database;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationService.Core;
using NotificationService.Core.Database;
using NotificationService.Infrastructure.Postgres.Database;
using NotificationService.Infrastructure.Postgres.Retention;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace NotificationService.Infrastructure.Postgres;

public static class Registration
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextPool<NotificationDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);

            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Явно фиксируем schema для __EFMigrationsHistory — defensive против
            // ситуации, когда WolverineFx ENtityFrameworkCore интеграция создаёт
            // history-table в `public` schema, и потом EF туда же пишет migrations.
            // Без этого первый deploy на fresh-DB кладёт history в public вместо
            // notifications, и при повторном запуске efbundle падает с
            // `relation "notifications" already exists` (EF не находит applied
            // migrations в notifications.__EFMigrationsHistory и пытается catch up
            // с нуля).
            options.UsePlatformNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"));

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<INotificationsRepository, NotificationsRepository>();
        services.AddScoped<IUserChannelsRepository, UserChannelsRepository>();
        services.AddScoped<IUserOptOutsRepository, UserOptOutsRepository>();
        services.AddScoped<IDeliveriesRepository, DeliveriesRepository>();
        // Прямая регистрация per-interface (вместо lambda-factory через concrete type) —
        // Wolverine code-gen на 6.0 будет throw'ить на 'opaque lambda factory' patterns.
        // SubscriptionsRepository stateless (state в scoped DbContext), so разные instance'ы
        // per interface within one scope не проблема — DbContext один и тот же.
        services.AddScoped<ISubscriptionsRepository, SubscriptionsRepository>();
        services.AddScoped<ISubscribersQuery, SubscriptionsRepository>();
        services.AddScoped<IWebPushSubscriptionsRepository, WebPushSubscriptionsRepository>();

        // Дедуп доставки дайджеста по физическому инбоксу (Gmail-алиасы → один ящик).
        services.AddScoped<IDigestEmailDedupStore, Digest.DigestEmailDedupStore>();

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<NotificationDbContext>, DbContextOutbox<NotificationDbContext>>();

        // Фоновая ретенция inbox'а. Config: `Notifications:Retention:*` (см. NotificationRetentionOptions).
        services.AddHostedService<NotificationRetentionService>();

        // Еженедельный дайджест «что нового за неделю» (#468).
        // Config: `Notifications:Digest:*` (см. NotificationDigestOptions).
        services.AddHostedService<Digest.WeeklyDigestService>();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;
    }
}
