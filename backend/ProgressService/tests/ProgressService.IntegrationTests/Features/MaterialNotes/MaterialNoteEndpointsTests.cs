using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Notes;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.MaterialNotes;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialNoteEndpointsTests : ProgressServiceTestsBase
{
    public MaterialNoteEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetNote_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(NoteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutNote_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(Guid.NewGuid()),
            new UpsertMaterialNoteRequest("Текст заметки"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteNote_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(NoteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutNote_ThenGet_ReturnsSavedNote()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest("Мои мысли по материалу"));

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        MaterialNoteResponse saved = await ReadWrappedResultAsync<MaterialNoteResponse>(putResponse);
        Assert.Equal(materialId, saved.MaterialId);
        Assert.Equal("Мои мысли по материалу", saved.Content);

        HttpResponseMessage getResponse = await AppHttpClient.GetAsync(NoteUrl(materialId));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        MaterialNoteResponse fetched = await ReadWrappedResultAsync<MaterialNoteResponse>(getResponse);
        Assert.Equal(materialId, fetched.MaterialId);
        Assert.Equal("Мои мысли по материалу", fetched.Content);

        MaterialNote stored = await ExecuteInDb(async db => await db.MaterialNotes.SingleAsync());
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(materialId, stored.MaterialId);
    }

    [Fact]
    public async Task PutNote_Twice_UpdatesContentAndKeepsSingleRow()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage firstResponse = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest("Первая версия"));
        HttpResponseMessage secondResponse = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest("Вторая версия"));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        MaterialNoteResponse updated = await ReadWrappedResultAsync<MaterialNoteResponse>(secondResponse);
        Assert.Equal("Вторая версия", updated.Content);

        MaterialNote stored = await ExecuteInDb(async db => await db.MaterialNotes.SingleAsync());
        Assert.Equal("Вторая версия", stored.Content);
        Assert.True(stored.UpdatedAt >= stored.CreatedAt);
    }

    [Fact]
    public async Task PutNote_ContentExceedingLimit_ReturnsBadRequest()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest(new string('а', MaterialNote.MAX_CONTENT_LENGTH + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        int notesCount = await ExecuteInDb(async db => await db.MaterialNotes.CountAsync());
        Assert.Equal(0, notesCount);
    }

    [Fact]
    public async Task PutNote_ContentAtLimit_IsSaved()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest(new string('а', MaterialNote.MAX_CONTENT_LENGTH)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MaterialNote stored = await ExecuteInDb(async db => await db.MaterialNotes.SingleAsync());
        Assert.Equal(MaterialNote.MAX_CONTENT_LENGTH, stored.Content.Length);
    }

    [Fact]
    public async Task DeleteNote_ThenGet_Returns404()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest("Удалить меня"));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync(NoteUrl(materialId));
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await AppHttpClient.GetAsync(NoteUrl(materialId));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        int notesCount = await ExecuteInDb(async db => await db.MaterialNotes.CountAsync());
        Assert.Equal(0, notesCount);
    }

    [Fact]
    public async Task DeleteNote_WhenMissing_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(NoteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNote_WhenMissing_Returns404()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(NoteUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetNote_DoesNotReturnOtherUsersNote()
    {
        Guid ownerId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(ownerId, "platform-participant");

        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            NoteUrl(materialId),
            new UpsertMaterialNoteRequest("Приватная заметка владельца"));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        AuthenticateAs(otherUserId, "platform-participant");

        HttpResponseMessage getResponse = await AppHttpClient.GetAsync(NoteUrl(materialId));

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    private static string NoteUrl(Guid materialId) => $"/progress/materials/{materialId}/note";
}
