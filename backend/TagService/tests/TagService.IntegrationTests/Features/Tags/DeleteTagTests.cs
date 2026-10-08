using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class DeleteTagTests : TagServiceTestsBase
{
    public DeleteTagTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task DeleteTag_WithExistingTag_ShouldDeleteTag_AndCascadeRelations()
    {
        var canonicalTag = await CreateTagAsync("csharp");
        var aliasTag = await CreateTagAsync("c-sharp");

        await CreateLinkAsync(canonicalTag.Id.Value);
        await CreateAliasAsync(canonicalTag.Id.Value, aliasTag.Id.Value);

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/tags/{canonicalTag.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(canonicalTag.Id.Value, envelope.Result);

        int deletedTagCount = await ExecuteInDb(db =>
            db.Tags.CountAsync(x => x.Id == canonicalTag.Id));
        Assert.Equal(0, deletedTagCount);

        int deletedLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x => x.TagId == canonicalTag.Id));
        Assert.Equal(0, deletedLinksCount);

        int deletedAliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x => x.TagId == canonicalTag.Id || x.AliasTagId == canonicalTag.Id));
        Assert.Equal(0, deletedAliasesCount);

        int aliasTagStillExistsCount = await ExecuteInDb(db =>
            db.Tags.CountAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(1, aliasTagStillExistsCount);

        Tag restoredAlias = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(TagKind.CANON, restoredAlias.Kind);
    }

    [Fact]
    public async Task DeleteTag_WithEmptyId_ShouldReturnBadRequest()
    {
        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/tags/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTag_WithNonExistentId_ShouldReturnOk()
    {
        // Delete is idempotent — if the tag doesn't exist, DeleteTagAsync's ExecuteDeleteAsync
        // silently deletes 0 rows and the handler returns success. This differs from Update/Merge,
        // which return 404 when the target tag is missing.
        Guid tagId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/tags/{tagId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(tagId, envelope.Result);
    }

    [Fact]
    public async Task DeleteCanonicalTag_WaitsForConcurrentMerge_AndRestoresCommittedAlias()
    {
        Tag canonicalTag = await CreateTagAsync("concurrent-canonical");
        Tag aliasTag = await CreateTagAsync("concurrent-alias");

        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@tag_id::text, 0));",
            connection,
            transaction))
        {
            lockCommand.Parameters.AddWithValue("tag_id", canonicalTag.Id.Value);
            await lockCommand.ExecuteNonQueryAsync();
        }

        Task<HttpResponseMessage> deleteTask = AppHttpClient.DeleteAsync($"/tags/{canonicalTag.Id.Value}");
        Task firstCompletion = await Task.WhenAny(deleteTask, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.NotSame(deleteTask, firstCompletion);

        await using (var mergeCommand = new NpgsqlCommand("""
            UPDATE tags
            SET kind = 'ALIAS'
            WHERE id = @AliasTagId;

            INSERT INTO tag_aliases (id, tag_id, alias_tag_id)
            VALUES (@RelationId, @CanonicalTagId, @AliasTagId);
            """, connection, transaction))
        {
            mergeCommand.Parameters.AddWithValue("RelationId", Guid.CreateVersion7());
            mergeCommand.Parameters.AddWithValue("CanonicalTagId", canonicalTag.Id.Value);
            mergeCommand.Parameters.AddWithValue("AliasTagId", aliasTag.Id.Value);
            await mergeCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        HttpResponseMessage response = await deleteTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Tag restoredAlias = await ExecuteInDb(db => db.Tags.SingleAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(TagKind.CANON, restoredAlias.Kind);
        Assert.Equal(0, await ExecuteInDb(db => db.TagAliases.CountAsync(x => x.AliasTagId == aliasTag.Id)));
    }
}
