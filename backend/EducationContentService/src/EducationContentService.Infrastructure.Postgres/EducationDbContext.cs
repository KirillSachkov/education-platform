using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.Roadmaps;
using EducationContentService.Domain.ShortLinks;
using Wolverine.EntityFrameworkCore;

namespace EducationContentService.Infrastructure.Postgres;

public class EducationDbContext : DbContext
{
    public EducationDbContext(DbContextOptions<EducationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseMaterial> CourseMaterials => Set<CourseMaterial>();

    public DbSet<CourseQuiz> CourseQuizzes => Set<CourseQuiz>();

    public DbSet<CourseItem> CourseItems => Set<CourseItem>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<Module> Modules => Set<Module>();

    public DbSet<ModuleItem> ModuleItems => Set<ModuleItem>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectItem> ProjectItems => Set<ProjectItem>();

    public DbSet<Issue> Issues => Set<Issue>();

    // Phase 5 (#15) — AI review configuration aggregates.
    public DbSet<ProjectReviewContext> ProjectReviewContexts => Set<ProjectReviewContext>();

    public DbSet<ReviewSpec> ReviewSpecs => Set<ReviewSpec>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();

    public DbSet<RoadmapNode> RoadmapNodes => Set<RoadmapNode>();

    public DbSet<RoadmapEdge> RoadmapEdges => Set<RoadmapEdge>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionSection> CollectionSections => Set<CollectionSection>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("education");
        modelBuilder.HasSequence<long>("asset_ownership_revision_seq", "education");
        modelBuilder.MapWolverineEnvelopeStorage("education");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EducationDbContext).Assembly);
    }
}
