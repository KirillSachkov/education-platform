using Core.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrainerService.Core.Database;
using TrainerService.Infrastructure.Postgres.Repositories;
using SharedKernel.DomainEvents;

using PlatformDatabase;
namespace TrainerService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContextPool<TrainerServiceDbContext>(options =>
        {
            options.UsePlatformNpgsql(connectionString);
        });

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<ITracksRepository, TracksRepository>();
        services.AddScoped<ITopicsRepository, TopicsRepository>();
        services.AddScoped<ITopicBanksRepository, TopicBanksRepository>();
        services.AddScoped<ITrainingSessionsRepository, TrainingSessionsRepository>();
        services.AddScoped<ITopicMasteryRepository, TopicMasteryRepository>();
        services.AddScoped<IBookmarkedQuestionsRepository, BookmarkedQuestionsRepository>();
        services.AddScoped<IAiFeedbackRatingsRepository, AiFeedbackRatingsRepository>();
        services.AddScoped<IQuestionStudyStatesRepository, QuestionStudyStatesRepository>();
        services.AddScoped<IMockInterviewsRepository, MockInterviewsRepository>();
        services.AddScoped<IAiUsageRepository, AiUsageRepository>();
        services.AddScoped<ITrainerQuestionsRepository, TrainerQuestionsRepository>();
        services.AddScoped<ITopicCoverageReader, TopicCoverageReader>();
        services.AddScoped<IStatSnapshotRepository, StatSnapshotRepository>();
        services.AddDomainEvents(typeof(DependencyInjectionExtensions).Assembly);

        return services;
    }
}
