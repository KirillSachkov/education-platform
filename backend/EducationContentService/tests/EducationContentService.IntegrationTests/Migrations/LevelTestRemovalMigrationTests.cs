using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Ordering;
using Testcontainers.PostgreSql;

namespace EducationContentService.IntegrationTests.Migrations;

public class LevelTestRemovalMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrate_FreshOrExistingDatabase_RemovesLevelTestAndPreservesContent(bool upgrade)
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            SearchPath = "education,public",
        };
        var options = new DbContextOptionsBuilder<EducationDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var db = new EducationDbContext(options);
        // Match the database bootstrap: trigram indexes need pg_trgm in the public schema.
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS education; CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public");
        Guid levelQuizId = Guid.Empty;
        Guid learningQuizId = Guid.Empty;

        if (upgrade)
        {
            await db.GetService<IMigrator>().MigrateAsync("20260711212217_AddCommentTargetOwnershipContractV1");
            Guid authorId = Guid.NewGuid();
            Quiz retired = Quiz.Create(authorId, Title.Create("Retired test").Value, []).Value;
            Quiz learning = Quiz.Create(authorId, Title.Create("Learning quiz").Value,
                [QuizQuestion.Create(Guid.NewGuid(), QuizQuestionType.EXACT_TEXT,
                    "Вопрос", [], [], "True False", explanation: "Пояснение").Value]).Value;
            levelQuizId = retired.Id;
            learningQuizId = learning.Id;
            Quiz historicalTrainer = Quiz.Create(authorId, Title.Create("Historical trainer").Value, [],
                purpose: QuizPurpose.TRAINER).Value;
            db.Quizzes.AddRange(retired, learning, historicalTrainer);
            var material = new Material(authorId, Title.Create("Lesson").Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.AttachQuiz(levelQuizId);
            db.Materials.Add(material);
            var module = new Module(authorId, Title.Create("Module").Value);
            db.Modules.Add(module);
            var collection = new Collection(authorId, Title.Create("Collection").Value);
            db.Collections.Add(collection);
            var section = new CollectionSection(collection.Id, null, null, SortKey.Initial());
            db.CollectionSections.Add(section);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE education.quizzes SET purpose = 'LEVEL_TEST', level_test_config = jsonb_build_object()
                WHERE id = {levelQuizId};
                INSERT INTO education.course_quizzes (id, course_id, quiz_id, sort_key)
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {levelQuizId}, 'a');
                INSERT INTO education.module_items
                    (id, module_id, item_type, reference_id, sort_key, is_optional, view_priority)
                VALUES ({Guid.NewGuid()}, {module.Id}, 'Quiz', {levelQuizId}, 'a', false, 'Key');
                INSERT INTO education.collection_items (id, section_id, item_type, reference_id, sort_key)
                VALUES ({Guid.NewGuid()}, {section.Id}, 'QUIZ', {levelQuizId}, 'a');
                """);
            db.ChangeTracker.Clear();
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.False(await db.Quizzes.AnyAsync(q => q.Id == levelQuizId));
        Assert.Equal(upgrade ? 2 : 0, await db.Quizzes.CountAsync());
        Assert.Equal(0, (int)QuizPurpose.MATERIAL_CHECK);
        Assert.Equal(2, (int)QuizPurpose.TRAINER);
        Assert.False(await db.CourseQuizzes.AnyAsync());
        Assert.False(await db.ModuleItems.AnyAsync());
        Assert.False(await db.CollectionItems.AnyAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.Collections.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.Modules.CountAsync());
        if (upgrade)
        {
            Assert.Equal(QuizPurpose.TRAINER,
                (await db.Quizzes.SingleAsync(q => q.Purpose == QuizPurpose.TRAINER)).Purpose);
            Assert.Null((await db.Materials.SingleAsync()).QuizId);
            Quiz learning = await db.Quizzes.SingleAsync(q => q.Id == learningQuizId);
            Assert.Equal("True False", Assert.Single(learning.Questions).ReferenceAnswer);
            Assert.Equal("Пояснение", learning.Questions[0].Explanation);
        }

        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'education' AND table_name = 'quizzes' AND column_name = 'level_test_config'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }
}
