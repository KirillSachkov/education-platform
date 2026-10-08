using AccessService.Domain;
using AccessService.Domain.Billing;
using AccessService.Domain.HomePins;
using AccessService.Domain.Onboarding;
using AccessService.Domain.TgJoinReminders;

namespace AccessService.Infrastructure.Postgres;

public sealed class AccessServiceDbContext : DbContext
{
    public const string SCHEMA_NAME = "access";

    public AccessServiceDbContext(DbContextOptions<AccessServiceDbContext> options) : base(options) { }

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanCourse> PlanCourses => Set<PlanCourse>();

    public DbSet<InviteLink> InviteLinks => Set<InviteLink>();

    public DbSet<PlanGrant> PlanGrants => Set<PlanGrant>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<BillingConfig> BillingConfigs => Set<BillingConfig>();

    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    public DbSet<Core.Domain.IdempotencyKey> IdempotencyKeys =>
        Set<Core.Domain.IdempotencyKey>();

    public DbSet<InviteRedemption> InviteRedemptions => Set<InviteRedemption>();

    public DbSet<PlanOnboardingFlow> PlanOnboardingFlows => Set<PlanOnboardingFlow>();

    public DbSet<PlanOnboardingStep> PlanOnboardingSteps => Set<PlanOnboardingStep>();

    public DbSet<UserPlanOnboarding> UserPlanOnboardings => Set<UserPlanOnboarding>();

    public DbSet<PlanPinnedMaterial> PlanPinnedMaterials => Set<PlanPinnedMaterial>();

    public DbSet<TgJoinReminder> TgJoinReminders => Set<TgJoinReminder>();

    public DbSet<Domain.Integrations.GitHub.AuthorGithubInstallation> AuthorGithubInstallations =>
        Set<Domain.Integrations.GitHub.AuthorGithubInstallation>();

    public DbSet<Domain.Integrations.GitHub.GithubOrgInvitation> GithubOrgInvitations =>
        Set<Domain.Integrations.GitHub.GithubOrgInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SCHEMA_NAME);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessServiceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
