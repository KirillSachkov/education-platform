using CommentService.Domain;
using CommentService.Web.Configuration;
using Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CommentService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class BackfillTargetAuthorIdCliTests : CommentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public BackfillTargetAuthorIdCliTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RunAsync_UsesCourseOwnerForCollaboratorCreatedBoundTargets()
    {
        Guid courseId = Guid.CreateVersion7();
        Guid courseOwnerId = Guid.CreateVersion7();
        Guid collaboratorId = Guid.CreateVersion7();
        Guid projectId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        Guid issueId = Guid.CreateVersion7();
        Guid quizId = Guid.CreateVersion7();

        await CreateCommentAsync(
            MockSecondAuthorId, "course", null, collaboratorId, courseId, EntityType.Course);
        await CreateCommentAsync(
            MockSecondAuthorId, "material", null, collaboratorId, materialId, EntityType.Material);
        await CreateCommentAsync(
            MockSecondAuthorId, "issue", null, collaboratorId, issueId, EntityType.Issue);
        await CreateCommentAsync(
            MockSecondAuthorId, "quiz", null, collaboratorId, quizId, EntityType.Quiz);

        await using var connection = new NpgsqlConnection(_factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand setup = connection.CreateCommand();
        setup.CommandText = """
            UPDATE education.courses SET author_id = @courseOwnerId WHERE id = @courseId;
            UPDATE education.issues SET project_id = @projectId WHERE id = @issueId;
            INSERT INTO education.course_materials VALUES (@courseId, @materialId);
            INSERT INTO education.course_quizzes VALUES (@courseId, @quizId);
            INSERT INTO education.course_items VALUES (@courseId, 'Project', @projectId);
            """;
        setup.Parameters.AddWithValue("courseId", courseId);
        setup.Parameters.AddWithValue("courseOwnerId", courseOwnerId);
        setup.Parameters.AddWithValue("collaboratorId", collaboratorId);
        setup.Parameters.AddWithValue("projectId", projectId);
        setup.Parameters.AddWithValue("materialId", materialId);
        setup.Parameters.AddWithValue("issueId", issueId);
        setup.Parameters.AddWithValue("quizId", quizId);
        await setup.ExecuteNonQueryAsync();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _factory.DatabaseConnectionString,
            })
            .Build();

        await BackfillTargetAuthorIdCli.RunAsync(configuration);

        Guid[] owners = await ExecuteInDb(db => db.Comments
            .Select(x => x.TargetAuthorId!.Value)
            .ToArrayAsync());
        Assert.Equal(4, owners.Length);
        Assert.All(owners, owner => Assert.Equal(courseOwnerId, owner));
    }
}