using System.Net;
using System.Net.Http.Json;
using Common;
using PlatformAuth.Authorization;
using SharedKernel;
using TagService.Contracts.SearchLookup;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SearchLookupTests : TagServiceTestsBase
{
    public SearchLookupTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetEntityTagsSearchLookupBatch_WithExistingLinks_ShouldReturnGroupedLookupDtos()
    {
        Domain.Tags.Tag first = await CreateTagAsync("redis");
        Domain.Tags.Tag second = await CreateTagAsync("postgres");

        await CreateLinkAsync(first.Id.Value, entityId: TestEntityId);
        await CreateLinkAsync(second.Id.Value, entityId: TestEntityId);
        await CreateLinkAsync(second.Id.Value, entityId: SecondEntityId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entity-tags/batch",
            new GetEntityTagsSearchLookupRequest(EntityType.Material, [TestEntityId, SecondEntityId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<EntityTagsSearchLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<EntityTagsSearchLookupDto>>>();

        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(2, envelope.Result!.Count);

        EntityTagsSearchLookupDto firstEntity =
            Assert.Single(envelope.Result, x => x.EntityId == TestEntityId);
        EntityTagsSearchLookupDto secondEntity =
            Assert.Single(envelope.Result, x => x.EntityId == SecondEntityId);

        Assert.Equal(2, firstEntity.TagIds.Count);
        Assert.Contains("redis", firstEntity.TagTitles);
        Assert.Contains("postgres", firstEntity.TagTitles);
        Assert.Equal([second.Id.Value], secondEntity.TagIds);
        Assert.Equal(["postgres"], secondEntity.TagTitles);
    }

    [Fact]
    public async Task GetEntitiesTagsSearchLookupBatch_WithMixedEntityTypes_ShouldReturnLookupDtosPerEntity()
    {
        Domain.Tags.Tag materialTag = await CreateTagAsync("redis");
        Domain.Tags.Tag courseTag = await CreateTagAsync("architecture");
        Guid courseEntityId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        await CreateLinkAsync(materialTag.Id.Value, entityType: TestEntityType, entityId: TestEntityId);
        await CreateLinkAsync(courseTag.Id.Value, entityType: EntityType.Course.ToString().ToLowerInvariant(), entityId: courseEntityId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entities-tags/batch",
            new GetEntitiesTagsSearchLookupRequest(
            [
                new EntityTagsSearchLookupBatchItem(EntityType.Material, TestEntityId),
                new EntityTagsSearchLookupBatchItem(EntityType.Course, courseEntityId),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<EntityTagsSearchLookupBatchDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<EntityTagsSearchLookupBatchDto>>>();

        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(2, envelope.Result!.Count);

        EntityTagsSearchLookupBatchDto materialEntity =
            Assert.Single(envelope.Result, x => x.EntityType == EntityType.Material && x.EntityId == TestEntityId);
        EntityTagsSearchLookupBatchDto courseEntity =
            Assert.Single(envelope.Result, x => x.EntityType == EntityType.Course && x.EntityId == courseEntityId);

        Assert.Equal([materialTag.Id.Value], materialEntity.TagIds);
        Assert.Equal(["redis"], materialEntity.TagTitles);
        Assert.Equal([courseTag.Id.Value], courseEntity.TagIds);
        Assert.Equal(["architecture"], courseEntity.TagTitles);
    }

    [Fact]
    public async Task GetEntityTagsSearchLookupBatch_AnonymousUser_ReturnsUnauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entity-tags/batch",
            new GetEntityTagsSearchLookupRequest(EntityType.Material, [TestEntityId]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEntityTagsSearchLookupBatch_ParticipantRole_ReturnsForbidden()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entity-tags/batch",
            new GetEntityTagsSearchLookupRequest(EntityType.Material, [TestEntityId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEntitiesTagsSearchLookupBatch_AnonymousUser_ReturnsUnauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entities-tags/batch",
            new GetEntitiesTagsSearchLookupRequest(
            [
                new EntityTagsSearchLookupBatchItem(EntityType.Material, TestEntityId),
            ]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEntitiesTagsSearchLookupBatch_ParticipantRole_ReturnsForbidden()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/search/entities-tags/batch",
            new GetEntitiesTagsSearchLookupRequest(
            [
                new EntityTagsSearchLookupBatchItem(EntityType.Material, TestEntityId),
            ]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
