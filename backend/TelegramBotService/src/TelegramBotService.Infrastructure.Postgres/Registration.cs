using Core.Database;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramBotService.Core;
using TelegramBotService.Core.Database;
using TelegramBotService.Infrastructure.Postgres.Database;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace TelegramBotService.Infrastructure.Postgres;

public static class Registration
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextPool<TelegramBotDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);

            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // Явно фиксируем schema для __EFMigrationsHistory — defensive против
            // WolverineFx EntityFrameworkCore инициализации, которая может создать
            // history-table в `public` до того как HasDefaultSchema применится.
            // Без этого первый deploy на fresh-DB кладёт history в public вместо
            // telegrambot, и при повторном efbundle падает с
            // `relation "user_links" already exists`.
            options.UsePlatformNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TelegramBotConstants.DEFAULT_SCHEMA));

            // EnableSensitiveDataLogging would log Telegram user IDs (PII) and platform user GUIDs
            // as SQL parameter values. EnableDetailedErrors gives enough diagnostic value without
            // that exposure even in Development.
            if (hostEnvironment.IsDevelopment())
            {
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<IUserLinkRepository, UserLinkRepository>();
        services.AddScoped<IChatBindingRepository, ChatBindingRepository>();
        services.AddScoped<IBotDecisionRepository, BotDecisionRepository>();

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<TelegramBotDbContext>, DbContextOutbox<TelegramBotDbContext>>();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;
    }
}
