using TrainerService.Domain.AiUsage;
using TrainerService.Domain.Bookmarks;
using TrainerService.Domain.FeedbackRatings;
using TrainerService.Domain.MockInterviews;
using TrainerService.Domain.Questions;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.Snapshots;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.Topics;
using TrainerService.Domain.Tracks;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Infrastructure.Postgres;

public sealed class TrainerServiceDbContext : DbContext
{
    public TrainerServiceDbContext(DbContextOptions<TrainerServiceDbContext> options) : base(options) { }

    public DbSet<Track> Tracks => Set<Track>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<TopicBank> TopicBanks => Set<TopicBank>();

    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();

    public DbSet<TopicMastery> TopicMasteries => Set<TopicMastery>();

    public DbSet<BookmarkedQuestion> BookmarkedQuestions => Set<BookmarkedQuestion>();

    public DbSet<AiFeedbackRating> AiFeedbackRatings => Set<AiFeedbackRating>();

    public DbSet<QuestionStudyState> QuestionStudyStates => Set<QuestionStudyState>();

    public DbSet<MockInterview> MockInterviews => Set<MockInterview>();

    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    public DbSet<TrainerQuestion> TrainerQuestions => Set<TrainerQuestion>();

    public DbSet<TrainerQuestionOption> TrainerQuestionOptions => Set<TrainerQuestionOption>();

    public DbSet<DailyStatSnapshot> DailyStatSnapshots => Set<DailyStatSnapshot>();

    public DbSet<TopicMasterySnapshot> TopicMasterySnapshots => Set<TopicMasterySnapshot>();

    public DbSet<QuestionAccuracySnapshot> QuestionAccuracySnapshots => Set<QuestionAccuracySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trainer");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrainerServiceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
