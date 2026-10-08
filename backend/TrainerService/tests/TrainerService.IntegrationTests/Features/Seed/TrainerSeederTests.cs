using Core.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.IntegrationTests.Infrastructure;
using TrainerService.Web.Configuration;

namespace TrainerService.IntegrationTests.Features.Seed;

/// <summary>
///     Сидер таксономии тренажёра (<see cref="TrainerSeeder"/>) из embedded <c>trainer-seed.json</c>.
///     Покрывает мок-собес-путь (#568): сидер резолвит <c>topicSlugs</c> → темы → их вопросы в
///     курированный набор, так что засиженная симуляция реально стартует (не «нет вопросов»).
///     Сидер регистрируется только в CLI-провайдере — в тесте собираем его из тестового scope.
/// </summary>
public sealed class TrainerSeederTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Seed_creates_taxonomy_and_resolvable_mock_interviews()
    {
        TrainerSeedResult result = await RunSeederAsync();

        // Таксономия залита: треки/темы/вопросы созданы.
        Assert.True(result.TracksCreated > 0, "ожидались созданные треки");
        Assert.True(result.TopicsCreated > 0, "ожидались созданные темы");
        Assert.True(result.QuestionsCreated > 0, "ожидались созданные вопросы");

        // Засиженные мок-собесы видны в списке хаба и РЕАЛЬНО доступны (курированный набор резолвится
        // в существующие вопросы → QuestionCount > 0, иначе карточка была бы «вопросы готовятся»).
        AuthenticateAs("platform-participant");
        IReadOnlyList<MockInterviewSummaryDto> mocks =
            await ReadResultAsync<IReadOnlyList<MockInterviewSummaryDto>>(
                await Client.GetAsync("/trainer/mock-interviews"));

        Assert.NotEmpty(mocks);
        Assert.All(mocks, m => Assert.True(
            m.QuestionCount > 0,
            $"мок-собес '{m.Slug}' засижен без доступных вопросов (QuestionCount={m.QuestionCount})"));
    }

    [Fact]
    public async Task Seed_is_idempotent_on_rerun()
    {
        await RunSeederAsync();
        TrainerSeedResult second = await RunSeederAsync();

        // Повторный прогон без --force ничего не создаёт заново (всё уже существует → skip).
        Assert.Equal(0, second.TracksCreated);
        Assert.Equal(0, second.TopicsCreated);
        Assert.Equal(0, second.QuestionsCreated);
        Assert.True(second.TracksSkipped > 0);
    }

    /// <summary>
    ///     Собирает <see cref="TrainerSeeder"/> из тестового scope (он регистрируется только в CLI-провайдере)
    ///     и прогоняет сидинг. На пустой БД создаёт всё; на повторном прогоне без force — skip.
    /// </summary>
    private async Task<TrainerSeedResult> RunSeederAsync(bool force = false)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        var seeder = new TrainerSeeder(
            sp.GetRequiredService<ITracksRepository>(),
            sp.GetRequiredService<ITopicsRepository>(),
            sp.GetRequiredService<ITopicBanksRepository>(),
            sp.GetRequiredService<ITrainerQuestionsRepository>(),
            sp.GetRequiredService<IMockInterviewsRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<ILogger<TrainerSeeder>>());

        Result<TrainerSeedResult, Error> result = await seeder.SeedAsync(force, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.GetMessage() : null);
        return result.Value;
    }
}
