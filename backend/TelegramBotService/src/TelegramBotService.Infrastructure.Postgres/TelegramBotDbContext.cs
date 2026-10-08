using Microsoft.EntityFrameworkCore;
using TelegramBotService.Core;
using TelegramBotService.Domain.Audit;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Domain.UserLinks;
using Wolverine.EntityFrameworkCore;

namespace TelegramBotService.Infrastructure.Postgres;

public class TelegramBotDbContext : DbContext
{
    public DbSet<UserLink> UserLinks => Set<UserLink>();

    public DbSet<ChatBinding> ChatBindings => Set<ChatBinding>();

    public DbSet<BotDecision> BotDecisions => Set<BotDecision>();

    public TelegramBotDbContext(DbContextOptions<TelegramBotDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TelegramBotConstants.DEFAULT_SCHEMA);
        modelBuilder.MapWolverineEnvelopeStorage(TelegramBotConstants.DEFAULT_SCHEMA);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TelegramBotDbContext).Assembly);
    }
}
