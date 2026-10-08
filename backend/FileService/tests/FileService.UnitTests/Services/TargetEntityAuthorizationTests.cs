using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using NSubstitute;
using PlatformAuth.Middleware;
using SharedKernel;

namespace FileService.UnitTests.Services;

public sealed class TargetEntityAuthorizationTests
{
    [Fact]
    public async Task ContentCreator_IsAuthorized()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        client.GetEntityOwnershipAsync("material", materialId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(Guid.NewGuid(), Guid.NewGuid(), userId)));
        TargetEntityAuthorization sut = Create(client, userId, "platform-author");

        UnitResult<Error> result = await sut.AuthorizeAsync(
            TargetEntity.Of("material", materialId).Value,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ForeignContent_IsDenied()
    {
        Guid materialId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        client.GetEntityOwnershipAsync("material", materialId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
        TargetEntityAuthorization sut = Create(client, Guid.NewGuid(), "platform-author");

        UnitResult<Error> result = await sut.AuthorizeAsync(
            TargetEntity.Of("material", materialId).Value,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("target.not.owner", Assert.Single(result.Error.Messages).Code);
    }

    [Fact]
    public async Task SharedContentManager_IsAuthorized()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        client.GetEntityOwnershipAsync("material", materialId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    [Guid.NewGuid(), userId])));
        TargetEntityAuthorization sut = Create(client, userId, "platform-author");

        UnitResult<Error> result = await sut.AuthorizeAsync(
            TargetEntity.Of("material", materialId).Value,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AuthorIdFallback_IsIgnoredWhenAuthoritativeManagerListIsPresent()
    {
        Guid userId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        client.GetEntityOwnershipAsync("project", projectId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(
                    Guid.NewGuid(),
                    userId,
                    Guid.NewGuid(),
                    [Guid.NewGuid()])));
        TargetEntityAuthorization sut = Create(client, userId, "platform-author");

        UnitResult<Error> result = await sut.AuthorizeAsync(
            TargetEntity.Of("project", projectId).Value,
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task UserProfile_AllowsOnlySelf()
    {
        Guid userId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        TargetEntityAuthorization sut = Create(client, userId, "platform-participant");

        UnitResult<Error> own = await sut.AuthorizeAsync(
            TargetEntity.Of("user", userId).Value,
            CancellationToken.None);
        UnitResult<Error> foreign = await sut.AuthorizeAsync(
            TargetEntity.Of("profile", Guid.NewGuid()).Value,
            CancellationToken.None);

        Assert.True(own.IsSuccess);
        Assert.True(foreign.IsFailure);
        await client.DidNotReceive().GetEntityOwnershipAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_BypassesRemoteLookup()
    {
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        TargetEntityAuthorization sut = Create(client, Guid.NewGuid(), "platform-admin");

        UnitResult<Error> result = await sut.AuthorizeAsync(
            TargetEntity.Of("material", Guid.NewGuid()).Value,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await client.DidNotReceive().GetEntityOwnershipAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentModerator_BypassesContentLookupButNotForeignProfile()
    {
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        TargetEntityAuthorization sut = Create(client, Guid.NewGuid(), "platform-moderator");

        UnitResult<Error> content = await sut.AuthorizeAsync(
            TargetEntity.Of("material", Guid.NewGuid()).Value,
            CancellationToken.None);
        UnitResult<Error> profile = await sut.AuthorizeAsync(
            TargetEntity.Of("profile", Guid.NewGuid()).Value,
            CancellationToken.None);

        Assert.True(content.IsSuccess);
        Assert.True(profile.IsFailure);
        await client.DidNotReceive().GetEntityOwnershipAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentModerator_DoesNotBypassStrictManagerRead()
    {
        Guid materialId = Guid.NewGuid();
        IEducationContentServiceClient client = Substitute.For<IEducationContentServiceClient>();
        client.GetEntityOwnershipAsync("material", materialId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()])));
        TargetEntityAuthorization sut = Create(client, Guid.NewGuid(), "platform-moderator");

        UnitResult<Error> result = await sut.AuthorizeManagerAsync(
            TargetEntity.Of("material", materialId).Value,
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private static TargetEntityAuthorization Create(
        IEducationContentServiceClient client,
        Guid userId,
        params string[] roles)
    {
        var user = new UserScopedData();
        user.Authenticate(userId, "Test", "test@example.com", roles);
        return new TargetEntityAuthorization(client, user);
    }
}
