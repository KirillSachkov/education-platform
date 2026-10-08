using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Infrastructure.Postgres;

public sealed class AssignmentReviewServiceDbContext : DbContext
{
    public const string SCHEMA_NAME = "assignment_review";

    public AssignmentReviewServiceDbContext(DbContextOptions<AssignmentReviewServiceDbContext> options)
        : base(options) { }

    public DbSet<VcsInstallation> VcsInstallations => Set<VcsInstallation>();

    public DbSet<AiReview> AiReviews => Set<AiReview>();

    public DbSet<AiReviewIteration> AiReviewIterations => Set<AiReviewIteration>();

    public DbSet<AiReviewIterationFeedback> AiReviewIterationFeedbacks =>
        Set<AiReviewIterationFeedback>();

    public DbSet<StudentPrMessage> StudentPrMessages => Set<StudentPrMessage>();

    public DbSet<AiModelSettings> AiModelSettings => Set<AiModelSettings>();

    public DbSet<IssueReviewSpec> IssueReviewSpecs => Set<IssueReviewSpec>();

    public DbSet<ProjectReviewGuidelines> ProjectReviewGuidelines => Set<ProjectReviewGuidelines>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SCHEMA_NAME);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssignmentReviewServiceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
