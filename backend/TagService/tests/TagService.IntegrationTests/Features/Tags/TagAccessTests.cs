using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Ownership;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PlatformAuth.Authorization;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TagAccessTests : TagServiceTestsBase
{
    public TagAccessTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetTags_Anonymous_ShouldReturnOk()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateTag_Anonymous_ShouldReturnUnauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", new CreateTagRequest
        {
            Title = "test"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTag_AsAuthor_ShouldReturnOk()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.AUTHOR);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", new CreateTagRequest
        {
            Title = "author-owned-tag"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateTag_AsParticipant_ShouldReturnForbidden()
    {
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", new CreateTagRequest
        {
            Title = "test"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_AsParticipant_ShouldReturnForbidden()
    {
        Tag tag = await CreateTagAsync("participant-tag");
        AuthenticateAs(Guid.CreateVersion7(), PlatformRoles.PARTICIPANT);

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_AsAuthorForOwnResource_ShouldReturnOk()
    {
        Guid authorId = Guid.CreateVersion7();
        Tag tag = await CreateTagAsync("own-resource-tag", authorId: authorId);
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, authorId, authorId, [authorId])));

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == tag.Id));
        Assert.Equal(1, linksCount);
    }

    [Fact]
    public async Task AddTags_AsAuthorForForeignResource_ShouldReturnForbidden()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid resourceOwnerId = Guid.CreateVersion7();
        Tag tag = await CreateTagAsync("foreign-resource-tag", authorId: authorId);
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, resourceOwnerId, resourceOwnerId, [resourceOwnerId])));

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WhenCallerMatchesAuthorButNotManagerPolicy_ShouldReturnForbidden()
    {
        Guid courseOwnerId = Guid.CreateVersion7();
        Guid directAuthorId = Guid.CreateVersion7();
        Tag tag = await CreateTagAsync("direct-author-policy", authorId: courseOwnerId);
        AuthenticateAs(courseOwnerId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, courseOwnerId, directAuthorId, [directAuthorId])));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value],
            TagTitles = []
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await ExecuteInDb(db => db.EntityTags.CountAsync()));
    }

    [Fact]
    public async Task AddTags_AsAuthorWithForeignTagId_ShouldReturnForbidden()
    {
        Guid authorId = Guid.CreateVersion7();
        Tag foreignTag = await CreateTagAsync("foreign-tag-id", authorId: Guid.CreateVersion7());
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, authorId, authorId, [authorId])));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [foreignTag.Id.Value],
            TagTitles = []
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await ExecuteInDb(db => db.EntityTags.CountAsync()));
    }

    [Fact]
    public async Task AddTags_AsAuthorWithTitleOwnedByAnotherAuthor_ShouldCreateOwnTag()
    {
        Guid authorId = Guid.CreateVersion7();
        Tag foreignTag = await CreateTagAsync("shared-title", authorId: Guid.CreateVersion7());
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, authorId, authorId, [authorId])));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [foreignTag.Title.Value]
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Tag ownTag = await ExecuteInDb(db => db.Tags.SingleAsync(x => x.AuthorId == authorId));
        Assert.NotEqual(foreignTag.Id, ownTag.Id);
        Assert.Equal(1, await ExecuteInDb(db => db.EntityTags.CountAsync(x => x.TagId == ownTag.Id)));
    }

    [Fact]
    public async Task RemoveTags_AsAuthorForOwnResource_ShouldReturnOk()
    {
        Guid authorId = Guid.CreateVersion7();
        Tag tag = await CreateTagAsync("remove-own-tag");
        await CreateLinkAsync(tag.Id.Value);
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, authorId, authorId, [authorId])));

        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoveTags_AsAuthorForForeignResource_ShouldReturnForbidden()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid resourceOwnerId = Guid.CreateVersion7();
        Tag tag = await CreateTagAsync("remove-foreign-tag");
        await CreateLinkAsync(tag.Id.Value);
        AuthenticateAs(authorId, PlatformRoles.AUTHOR);
        EducationContentClient.GetEntityOwnershipAsync(
                TestEntityType, TestEntityId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, resourceOwnerId, resourceOwnerId, [resourceOwnerId])));

        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Assert.Equal(1, await ExecuteInDb(db => db.EntityTags.CountAsync(x => x.TagId == tag.Id)));
    }
}
