using ProgressService.Domain.AuthorQuestions;
using ProgressService.Domain.ContentAccess;
using ProgressService.Domain.CoursePositions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Bookmarks;
using ProgressService.Domain.Issues;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.Domain.Quizzes;
using Wolverine.EntityFrameworkCore;

namespace ProgressService.Infrastructure.Postgres;

public class ProgressDbContext : DbContext
{
    public ProgressDbContext(DbContextOptions<ProgressDbContext> options)
        : base(options)
    {
    }

    public DbSet<ContentGrant> ContentGrants => Set<ContentGrant>();

    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();

    public DbSet<ModuleProgress> ModuleProgresses => Set<ModuleProgress>();

    public DbSet<ModuleItemProgress> ModuleItemProgresses => Set<ModuleItemProgress>();

    public DbSet<MaterialView> MaterialViews => Set<MaterialView>();

    public DbSet<AnonymousMaterialView> AnonymousMaterialViews => Set<AnonymousMaterialView>();

    public DbSet<ProjectProgress> ProjectProgresses => Set<ProjectProgress>();

    public DbSet<IssueProgress> IssueProgresses => Set<IssueProgress>();

    public DbSet<IssueSubmission> IssueSubmissions => Set<IssueSubmission>();

    public DbSet<MaterialBookmark> MaterialBookmarks => Set<MaterialBookmark>();

    public DbSet<IssueAuthorQuestion> IssueAuthorQuestions => Set<IssueAuthorQuestion>();

    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();

    public DbSet<CoursePosition> CoursePositions => Set<CoursePosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("progress");
        modelBuilder.MapWolverineEnvelopeStorage("progress");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProgressDbContext).Assembly);
    }
}