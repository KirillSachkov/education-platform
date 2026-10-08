using CSharpFunctionalExtensions;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SharedKernel;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     CLI-команда идемпотентного сидинга level-test квиза
///     «Определи свой уровень .NET-разработчика» из <c>SeedData/level-test.json</c>.
///
///     Usage: dotnet EducationContentService.Web.dll seed-level-test [--force]
///
///     Повторный запуск безопасен: квиз с фиксированным
///     <see cref="LevelTestSeeder.SeedQuizId"/> уже существует → по умолчанию НЕ трогается
///     (владелец редактирует тест из UI «Тест уровня», #487 — правки переживают re-seed),
///     лог + exit 0. Полная перезапись содержимым seed-файла — только с явным
///     <c>--force</c> (см. семантику в <see cref="LevelTestSeeder"/>). Маппинг секций на
///     курсы (recommendedCourseId/fallbackCourseId) в seed не входит — курсы
///     env-специфичны, владелец настраивает их в редакторе «Тест уровня» (или через
///     author update API). При ошибке бросает исключение → процесс завершается
///     ненулевым кодом.
/// </summary>
public static class SeedLevelTestCli
{
    public const string CommandName = "seed-level-test";

    public const string FORCE_FLAG = "--force";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static bool HasForceFlag(string[] args) =>
        args.Any(x => string.Equals(x, FORCE_FLAG, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSingleton(hostEnvironment);
        services.AddDbContext<EducationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });
        services.AddScoped<LevelTestSeeder>();

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        LevelTestSeeder seeder = scope.ServiceProvider.GetRequiredService<LevelTestSeeder>();
        Result<LevelTestSeedResult, Error> result = await seeder.SeedAsync(force, cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"seed-level-test failed: {result.Error.GetMessage()}");
        }

        Log.Information(
            "seed-level-test done: quiz {QuizId} {Action}, {QuestionCount} questions",
            result.Value.QuizId,
            result.Value.Action,
            result.Value.QuestionCount);
    }
}
