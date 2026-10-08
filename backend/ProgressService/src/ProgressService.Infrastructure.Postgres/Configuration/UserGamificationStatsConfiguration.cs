using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Gamification;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class UserGamificationStatsConfiguration : IEntityTypeConfiguration<UserGamificationStats>
{
    public const string USER_INDEX = "ux_user_gamification_stats_user_id";
    public const string LEADERBOARD_INDEX = "ix_user_gamification_stats_leaderboard";

    public void Configure(EntityTypeBuilder<UserGamificationStats> builder)
    {
        builder.ToTable("user_gamification_stats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.TotalXp)
            .IsRequired()
            .HasColumnName("total_xp");

        builder.Property(x => x.CurrentLevel)
            .IsRequired()
            .HasColumnName("current_level");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName(USER_INDEX)
            .IsUnique();

        builder.HasIndex(x => new { x.TotalXp, x.UpdatedAt, x.UserId })
            .IsDescending(true, false, false)
            .HasDatabaseName(LEADERBOARD_INDEX);
    }
}
