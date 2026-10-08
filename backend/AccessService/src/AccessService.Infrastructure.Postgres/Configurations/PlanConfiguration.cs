using System.Text.Json;
using AccessService.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("plans");

        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");

        b.Property(p => p.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        // Phase 1.3: Tier — единственный first-class классификатор. Колонка `kind`
        // удалена миграцией DropPlanKindColumn.
        b.Property(p => p.Tier)
            .HasColumnName("tier")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // OfferType — маркетинг-формат оффера (ортогонален tier). Строка, без
        // HasDefaultValue (домен всегда ставит значение в Create/UpdateOfferType).
        b.Property(p => p.OfferType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("offer_type")
            .IsRequired();

        // Scope — каталожный дискриминатор (#674), производная от OfferType. Строка, без
        // HasDefaultValue (домен всегда ставит значение в ResolveScope). DB-DEFAULT 'PLATFORM'
        // живёт в миграции AddPlanScope — страхует legacy-вставки/backfill, но EF всегда пишет
        // явное значение. См. PlanScope.
        b.Property(p => p.Scope)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("scope")
            .IsRequired();

        b.OwnsOne(p => p.Slug, sb =>
        {
            sb.Property(s => s.Value)
                .HasColumnName("slug")
                .HasMaxLength(PlanSlug.MAX_LENGTH)
                .IsRequired();
        });

        b.OwnsOne(p => p.DisplayName, nb =>
        {
            nb.Property(n => n.Value)
                .HasColumnName("display_name")
                .HasMaxLength(PlanDisplayName.MAX_LENGTH)
                .IsRequired();
        });

        b.Property(p => p.ShortDescription)
            .HasColumnName("short_description")
            .HasMaxLength(500)
            .IsRequired();

        b.Property(p => p.LongDescription)
            .HasColumnName("long_description")
            .IsRequired();

        b.Property(p => p.CoverFileId).HasColumnName("cover_file_id");

        ValueComparer<IReadOnlyList<string>> stringListComparer = new(
            (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c, StringComparer.Ordinal)),
            v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal))),
            v => (IReadOnlyList<string>)v.ToList());

        b.Property(p => p.Features)
            .HasColumnName("features")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        b.Property(p => p.PriceCents).HasColumnName("price_cents");

        b.Property(p => p.Currency)
            .HasColumnName("currency")
            .HasMaxLength(8)
            .IsRequired();

        // Акция: процент скидки + окно дат. Все три nullable; вычисляются в read-time,
        // в SQL не фильтруются — индекс не нужен.
        b.Property(p => p.DiscountPercent).HasColumnName("discount_percent");
        b.Property(p => p.DiscountStartsAt).HasColumnName("discount_starts_at");
        b.Property(p => p.DiscountEndsAt).HasColumnName("discount_ends_at");

        // course_id column kept on `plans` (rollback escape hatch, #404) but no longer EF-mapped —
        // the bundle association lives in `access.plan_courses` via the Courses nav-collection.
        // FirstCourseId is a computed scalar projection over Courses — exclude it from the model.
        b.Ignore(p => p.FirstCourseId);

        b.HasMany(p => p.Courses)
            .WithOne()
            .HasForeignKey(c => c.PlanId)
            .HasPrincipalKey(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // Field-access — `Courses => _courses`. Без Field-mode EF читает navigation как
        // read-only IReadOnlyList и при Add к private `_courses` change tracker не видит
        // новый entity (state Modified вместо Added → 0-row UPDATE → DbUpdateConcurrencyException).
        b.Navigation(p => p.Courses)
            .AutoInclude()
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_courses");

        b.Property(p => p.IncludesFutureContent)
            .HasColumnName("includes_future_content")
            .IsRequired();

        b.Property(p => p.TrialDurationDays).HasColumnName("trial_duration_days");

        // Capabilities — bitmask `[Flags]` enum, хранится как int. Default = FULL.
        b.Property(p => p.Capabilities)
            .HasColumnName("capabilities")
            .HasConversion<int>()
            .IsRequired();

        b.OwnsOne(p => p.Term, tb =>
        {
            tb.Property(t => t.Kind)
                .HasColumnName("term_kind")
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            tb.Property(t => t.RecurringIntervalDays).HasColumnName("term_recurring_days");
        });

        b.Property(p => p.IsPublic)
            .HasColumnName("is_public")
            .IsRequired();

        b.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        b.Property(p => p.IsHighlighted)
            .HasColumnName("is_highlighted")
            .IsRequired();

        b.Property(p => p.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        b.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(p => p.ArchivedAt).HasColumnName("archived_at");

        b.Property(p => p.GitHubOrg)
            .HasColumnName("github_org_slug")
            .HasMaxLength(39);

        // Telegram-приветствие плана: постится ботом в группу при входе участника.
        // Nullable text, не фильтруется в SQL — индекс не нужен.
        b.Property(p => p.TelegramWelcomeMessage)
            .HasColumnName("telegram_welcome_message")
            .HasMaxLength(Plan.TELEGRAM_WELCOME_MAX_LENGTH);

        // Partial index: catalog reads filter on (is_public, is_active).
        b.HasIndex(p => p.IsPublic)
            .HasDatabaseName("ix_plans_is_public")
            .HasFilter("is_active");

        // Partial-unique on github_org_slug for active plans only — один org привязан
        // максимум к одному активному плану. Сам index создаётся в миграции через raw SQL,
        // потому что Fluent API не умеет combinated NOT NULL + archived_at IS NULL фильтр.

        // Legacy composite unique on (author_id, slug) lives in raw SQL inside the
        // AddAccessSchema migration. The current platform catalog additionally has a
        // raw partial unique index on public active `slug`.
    }
}
