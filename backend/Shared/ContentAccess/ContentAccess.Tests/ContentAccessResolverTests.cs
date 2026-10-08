using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace ContentAccess.Tests;

public class EntitlementCheckerTests
{
    private static readonly AccessSubject Admin = new(true, Guid.NewGuid(), IsAdmin: true);
    private static readonly AccessSubject AuthenticatedUser = new(true, Guid.NewGuid(), IsAdmin: false);
    private static readonly AccessSubject AnonymousUser = new(false, Guid.Empty, IsAdmin: false);

    private static readonly Guid LessonId = Guid.NewGuid();

    private static (Redis.RedisEntitlementChecker checker, IDatabase db) CreateChecker()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var checker = new Redis.RedisEntitlementChecker(redis);
        return (checker, db);
    }

    // -- Admin bypass --------------------------------------------------------

    [Fact]
    public async Task Admin_GrantsAccess_WithoutRedis()
    {
        var (checker, db) = CreateChecker();

        AccessDecision result = await checker.CheckAccessAsync(Admin, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ADMIN_OR_AUTHOR, result.Reason);
        await db.DidNotReceiveWithAnyArgs().SetLengthAsync(default);
    }

    [Fact]
    public async Task AuthorRole_DoesNotBypassChecks()
    {
        // Author (IsAdmin=false) should go through normal entitlement flow
        var author = new AccessSubject(true, Guid.NewGuid(), IsAdmin: false);
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns(["course:test"]);
        db.SetCombineAsync(SetOperation.Intersect, Arg.Any<RedisKey>(), Arg.Any<RedisKey>())
            .Returns([]);

        AccessDecision result = await checker.CheckAccessAsync(author, "material", LessonId);

        Assert.False(result.IsGranted);
    }

    // -- No tags -> DENIED (fail-closed) -------------------------------------

    [Fact]
    public async Task ResourceWithoutTags_DeniesAccess_ToAuthenticatedUser()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.RESOURCE_NOT_REGISTERED, result.Reason);
    }

    [Fact]
    public async Task ResourceWithoutTags_DeniesAccess_ToAnonymous()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([]);

        AccessDecision result = await checker.CheckAccessAsync(AnonymousUser, "material", LessonId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.RESOURCE_NOT_REGISTERED, result.Reason);
    }

    // -- Explicit PUBLIC tag -------------------------------------------------

    [Fact]
    public async Task ResourceWithPublicTag_GrantsAccess_ToAnonymous()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([GrantTags.PUBLIC]);

        AccessDecision result = await checker.CheckAccessAsync(AnonymousUser, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.PUBLIC, result.Reason);
    }

    [Fact]
    public async Task ResourceWithPublicTag_GrantsAccess_ToAuthenticated()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([GrantTags.PUBLIC]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.PUBLIC, result.Reason);
    }

    [Fact]
    public async Task ResourceCheck_LoadsResourceTagsInOneRedisRoundTrip()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([GrantTags.PUBLIC]);

        AccessDecision result = await checker.CheckAccessAsync(AnonymousUser, "material", LessonId);

        Assert.True(result.IsGranted);
        await db.Received(1).SetMembersAsync(Arg.Any<RedisKey>());
        await db.DidNotReceiveWithAnyArgs().SetLengthAsync(default);
        await db.DidNotReceive().SetContainsAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>());
    }

    // -- Authenticated tag ---------------------------------------------------

    [Fact]
    public async Task ResourceWithAuthenticatedTag_GrantsToAuthenticatedUser()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([GrantTags.AUTHENTICATED]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.AUTHENTICATED_ONLY, result.Reason);
    }

    [Fact]
    public async Task ResourceWithAuthenticatedTag_DeniesToAnonymous()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([GrantTags.AUTHENTICATED]);

        AccessDecision result = await checker.CheckAccessAsync(AnonymousUser, "material", LessonId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.NOT_AUTHENTICATED, result.Reason);
    }

    // -- Course tag ----------------------------------------------------------

    [Fact]
    public async Task ResourceWithCourseTag_GrantsToUserWithMatchingGrant()
    {
        Guid courseId = Guid.NewGuid();
        string tag = GrantTags.Course(courseId);

        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([tag]);
        db.SetCombineAsync(SetOperation.Intersect, Arg.Any<RedisKey>(), Arg.Any<RedisKey>())
            .Returns([(RedisValue)tag]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ENTITLEMENT, result.Reason);
    }

    [Fact]
    public async Task ResourceWithCourseTag_DeniesUserWithoutGrant()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns(["course:00000000-0000-0000-0000-000000000001"]);
        db.SetCombineAsync(SetOperation.Intersect, Arg.Any<RedisKey>(), Arg.Any<RedisKey>())
            .Returns([]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.NONE, result.Reason);
    }

    [Fact]
    public async Task ResourceWithTags_DeniesAnonymousUser()
    {
        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns(["course:00000000-0000-0000-0000-000000000001"]);

        AccessDecision result = await checker.CheckAccessAsync(AnonymousUser, "material", LessonId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.NOT_AUTHENTICATED, result.Reason);
    }

    // -- Multiple tags (OR logic) --------------------------------------------

    [Fact]
    public async Task ResourceWithMultipleTags_GrantsIfUserHasAny()
    {
        Guid courseId2 = Guid.NewGuid();
        string tag2 = GrantTags.Course(courseId2);

        var (checker, db) = CreateChecker();
        db.SetMembersAsync(Arg.Any<RedisKey>()).Returns([
            "course:00000000-0000-0000-0000-000000000001",
            tag2
        ]);
        db.SetCombineAsync(SetOperation.Intersect, Arg.Any<RedisKey>(), Arg.Any<RedisKey>())
            .Returns([(RedisValue)tag2]);

        AccessDecision result = await checker.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ENTITLEMENT, result.Reason);
    }

    // -- Redis unavailable -> denied (via ResilientEntitlementChecker) --------

    [Fact]
    public async Task RedisUnavailable_ReturnsDenied()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var inner = new Redis.RedisEntitlementChecker(redis);
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<Redis.ResilientEntitlementChecker>>();
        var resilient = new Redis.ResilientEntitlementChecker(inner, logger);

        AccessDecision result = await resilient.CheckAccessAsync(AuthenticatedUser, "material", LessonId);

        Assert.False(result.IsGranted);
    }
}
