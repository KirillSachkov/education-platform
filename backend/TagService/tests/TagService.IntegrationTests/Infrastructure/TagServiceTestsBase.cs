using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Common;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel;
using TagService.Domain.EntityTags;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;
using TagService.Infrastructure.Postgres;

namespace TagService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class TagServiceTestsBase : IAsyncLifetime
{
    protected const string TestEntityType = "material";
    protected static readonly EntityType TestEntityTypeValue = EntityType.Material;
    protected static readonly Guid TestEntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid SecondEntityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static readonly Guid TestAuthorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Func<Task> _resetDatabase;

    protected TagServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        Host = factory.Services.GetRequiredService<IHost>();
        EducationContentClient = factory.EducationContentClient;
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IntegrationTestsWebFactory Factory { get; init; }
    protected IServiceProvider Services { get; init; }
    protected IHost Host { get; init; }
    protected IEducationContentServiceClient EducationContentClient { get; init; }
    protected HttpClient AppHttpClient { get; init; }

    protected void AuthenticateAs(Guid userId, params string[] groups)
    {
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(userId, groups));
    }

    protected void AuthenticateAsAdmin(Guid? userId = null)
    {
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken(userId));
    }

    protected void RemoveAuthentication()
    {
        AppHttpClient.DefaultRequestHeaders.Authorization = null;
    }

    protected async Task<Tag> CreateTagAsync(string title, string? slug = null, Guid? authorId = null)
    {
        return await ExecuteInDb(async dbContext =>
        {
            Tag tag = Tag.Create(
                TagTitle.Of(title).Value,
                TagSlug.Of(slug ?? title).Value,
                authorId ?? TestAuthorId).Value;

            await dbContext.Tags.AddAsync(tag);
            await dbContext.SaveChangesAsync();

            return tag;
        });
    }

    protected async Task<TagAlias> CreateAliasAsync(Guid tagId, Guid aliasTagId)
    {
        return await ExecuteInDb(async dbContext =>
        {
            TagAlias alias = TagAlias.Create(
                TagId.Of(tagId),
                TagId.Of(aliasTagId)).Value;

            await dbContext.TagAliases.AddAsync(alias);
            await dbContext.SaveChangesAsync();

            return alias;
        });
    }

    protected async Task<HttpResponseMessage> DeleteAsJsonAsync<TRequest>(string url, TRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(request)
        };

        return await AppHttpClient.SendAsync(message);
    }

    protected async Task<Envelope<JsonElement>> ReadJsonEnvelopeAsync(HttpResponseMessage response)
    {
        Envelope<JsonElement>? envelope = await response.Content.ReadFromJsonAsync<Envelope<JsonElement>>();
        Assert.NotNull(envelope);
        return envelope;
    }

    protected static JsonElement GetRequiredProperty(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value))
                return value;
        }

        throw new Xunit.Sdk.XunitException($"None of properties [{string.Join(", ", names)}] found.");
    }

    protected async Task AssertConflictResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedMessage)
    {
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement root = document.RootElement;
        Assert.True(GetRequiredProperty(root, "isError", "IsError").GetBoolean());

        JsonElement error = GetRequiredProperty(root, "error", "Error");
        Assert.Equal("CONFLICT", GetRequiredProperty(error, "type", "Type").GetString());

        JsonElement firstMessage = GetRequiredProperty(error, "messages", "Messages")
            .EnumerateArray()
            .First();

        Assert.Equal(expectedCode, GetRequiredProperty(firstMessage, "code", "Code").GetString());
        Assert.Equal(expectedMessage, GetRequiredProperty(firstMessage, "message", "Message").GetString());
    }

    protected async Task<EntityTag> CreateLinkAsync(
        Guid tagId,
        string? entityType = null,
        Guid? entityId = null)
    {
        return await ExecuteInDb(async dbContext =>
        {
            TagEntityReference entityReference = TagEntityReference.Of(entityType ?? TestEntityType, entityId ?? TestEntityId).Value;
            EntityTag link = EntityTag.Create(entityReference, TagId.Of(tagId)).Value;

            await dbContext.EntityTags.AddAsync(link);
            await dbContext.SaveChangesAsync();

            return link;
        });
    }

    protected async Task ExecuteInDb(Func<TagDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        TagDbContext dbContext = scope.ServiceProvider.GetRequiredService<TagDbContext>();

        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<TagDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        TagDbContext dbContext = scope.ServiceProvider.GetRequiredService<TagDbContext>();

        return await action(dbContext);
    }

    public Task InitializeAsync()
    {
        AuthenticateAsAdmin(TestAuthorId);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        RemoveAuthentication();
        await _resetDatabase();
    }
}
