using ContentAccess.Redis;
using StackExchange.Redis;

namespace ContentAccess.Tests;

[Collection(nameof(RedisTestCollection))]
public class RedisIntegrationTests : IAsyncLifetime
{
    private readonly IConnectionMultiplexer _redis;

    public RedisIntegrationTests(RedisTestFixture fixture)
    {
        _redis = fixture.Redis;
    }

    public async Task InitializeAsync()
    {
        await _redis.GetDatabase().ExecuteAsync("FLUSHDB");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── RedisEntitlementChecker ─────────────────────────────────────

    [Fact]
    public async Task Checker_ResourceWithoutTags_DeniesAccess()
    {
        var checker = new RedisEntitlementChecker(_redis);
        var user = new AccessSubject(true, Guid.NewGuid(), false);

        AccessDecision result = await checker.CheckAccessAsync(user, "material", Guid.NewGuid());

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.RESOURCE_NOT_REGISTERED, result.Reason);
    }

    [Fact]
    public async Task Checker_ResourceWithPublicTag_GrantsAccessToAnonymous()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        var checker = new RedisEntitlementChecker(_redis);

        Guid resourceId = Guid.NewGuid();
        await writer.SetTagsAsync("material", resourceId, [GrantTags.PUBLIC]);

        var anon = new AccessSubject(false, Guid.Empty, false);
        AccessDecision result = await checker.CheckAccessAsync(anon, "material", resourceId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.PUBLIC, result.Reason);
    }

    [Fact]
    public async Task Checker_UserWithMatchingGrant_ReturnsEntitlement()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        var grantWriter = new RedisUserGrantWriter(_redis);
        var checker = new RedisEntitlementChecker(_redis);

        Guid resourceId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        string tag = GrantTags.Course(courseId);

        await writer.SetTagsAsync("material", resourceId, [tag]);
        await grantWriter.GrantAsync(userId, tag);

        var user = new AccessSubject(true, userId, false);
        AccessDecision result = await checker.CheckAccessAsync(user, "material", resourceId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ENTITLEMENT, result.Reason);
    }

    [Fact]
    public async Task Checker_UserWithoutGrant_ReturnsDenied()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        var checker = new RedisEntitlementChecker(_redis);

        Guid resourceId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        string tag = GrantTags.Course(courseId);

        await writer.SetTagsAsync("material", resourceId, [tag]);

        var user = new AccessSubject(true, Guid.NewGuid(), false);
        AccessDecision result = await checker.CheckAccessAsync(user, "material", resourceId);

        Assert.False(result.IsGranted);
        Assert.Equal(AccessReason.NONE, result.Reason);
    }

    [Fact]
    public async Task Checker_MultipleTagsUserHasOne_ReturnsEntitlement()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        var grantWriter = new RedisUserGrantWriter(_redis);
        var checker = new RedisEntitlementChecker(_redis);

        Guid resourceId = Guid.NewGuid();
        string tag1 = GrantTags.Course(Guid.NewGuid());
        string tag2 = GrantTags.Course(Guid.NewGuid());
        Guid userId = Guid.NewGuid();

        await writer.SetTagsAsync("material", resourceId, [tag1, tag2]);
        await grantWriter.GrantAsync(userId, tag2);

        var user = new AccessSubject(true, userId, false);
        AccessDecision result = await checker.CheckAccessAsync(user, "material", resourceId);

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ENTITLEMENT, result.Reason);
    }

    [Fact]
    public async Task Checker_AdminBypass_DoesNotQueryRedis()
    {
        var checker = new RedisEntitlementChecker(_redis);
        var admin = new AccessSubject(true, Guid.NewGuid(), IsAdmin: true);

        AccessDecision result = await checker.CheckAccessAsync(admin, "material", Guid.NewGuid());

        Assert.True(result.IsGranted);
        Assert.Equal(AccessReason.ADMIN_OR_AUTHOR, result.Reason);
    }

    // ── RedisResourceAccessWriter ──────────────────────────────────

    [Fact]
    public async Task ResourceWriter_SetTags_ReturnsExpectedMembers()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        Guid resourceId = Guid.NewGuid();
        string tag1 = GrantTags.Course(Guid.NewGuid());
        string tag2 = GrantTags.AUTHENTICATED;

        await writer.SetTagsAsync("material", resourceId, [tag1, tag2]);

        IDatabase db = _redis.GetDatabase();
        RedisValue[] members = await db.SetMembersAsync(EntitlementKeys.ResourceAccess("material", resourceId));
        string[] values = members.Select(m => m.ToString()).Order().ToArray();

        Assert.Equal(2, values.Length);
        Assert.Contains(tag1, values);
        Assert.Contains(tag2, values);
    }

    [Fact]
    public async Task ResourceWriter_AddTag_AppendsToSet()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        Guid resourceId = Guid.NewGuid();
        string tag1 = "tag1";
        string tag2 = "tag2";

        await writer.SetTagsAsync("material", resourceId, [tag1]);
        await writer.AddTagAsync("material", resourceId, tag2);

        IDatabase db = _redis.GetDatabase();
        long count = await db.SetLengthAsync(EntitlementKeys.ResourceAccess("material", resourceId));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ResourceWriter_RemoveTag_RemovesFromSet()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        Guid resourceId = Guid.NewGuid();
        string tag1 = "tag1";
        string tag2 = "tag2";

        await writer.SetTagsAsync("material", resourceId, [tag1, tag2]);
        await writer.RemoveTagAsync("material", resourceId, tag1);

        IDatabase db = _redis.GetDatabase();
        RedisValue[] members = await db.SetMembersAsync(EntitlementKeys.ResourceAccess("material", resourceId));
        Assert.Single(members);
        Assert.Equal(tag2, members[0].ToString());
    }

    [Fact]
    public async Task ResourceWriter_ClearTags_DeletesKey()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        Guid resourceId = Guid.NewGuid();

        await writer.SetTagsAsync("material", resourceId, ["tag1"]);
        await writer.ClearTagsAsync("material", resourceId);

        IDatabase db = _redis.GetDatabase();
        bool exists = await db.KeyExistsAsync(EntitlementKeys.ResourceAccess("material", resourceId));
        Assert.False(exists);
    }

    [Fact]
    public async Task ResourceWriter_SetTags_OverwritesPrevious()
    {
        var writer = new RedisResourceAccessWriter(_redis);
        Guid resourceId = Guid.NewGuid();

        await writer.SetTagsAsync("material", resourceId, ["old1", "old2"]);
        await writer.SetTagsAsync("material", resourceId, ["new1"]);

        IDatabase db = _redis.GetDatabase();
        RedisValue[] members = await db.SetMembersAsync(EntitlementKeys.ResourceAccess("material", resourceId));
        Assert.Single(members);
        Assert.Equal("new1", members[0].ToString());
    }

    // ── RedisUserGrantWriter ───────────────────────────────────────

    [Fact]
    public async Task UserGrantWriter_GrantAndGrantMany_AddsGrants()
    {
        var writer = new RedisUserGrantWriter(_redis);
        Guid userId = Guid.NewGuid();
        string tag1 = GrantTags.Course(Guid.NewGuid());
        string tag2 = GrantTags.Course(Guid.NewGuid());
        string tag3 = GrantTags.Course(Guid.NewGuid());

        await writer.GrantAsync(userId, tag1);
        await writer.GrantManyAsync(userId, [tag2, tag3]);

        IDatabase db = _redis.GetDatabase();
        long count = await db.SetLengthAsync(EntitlementKeys.UserGrants(userId));
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task UserGrantWriter_RevokeAndRevokeMany_RemovesGrants()
    {
        var writer = new RedisUserGrantWriter(_redis);
        Guid userId = Guid.NewGuid();
        string tag1 = "g1";
        string tag2 = "g2";
        string tag3 = "g3";

        await writer.GrantManyAsync(userId, [tag1, tag2, tag3]);
        await writer.RevokeAsync(userId, tag1);
        await writer.RevokeManyAsync(userId, [tag2]);

        IDatabase db = _redis.GetDatabase();
        RedisValue[] members = await db.SetMembersAsync(EntitlementKeys.UserGrants(userId));
        Assert.Single(members);
        Assert.Equal(tag3, members[0].ToString());
    }

    [Fact]
    public async Task EntitlementReader_ReturnsUserGrantTags()
    {
        var writer = new RedisUserGrantWriter(_redis);
        var reader = new RedisEntitlementReader(_redis);
        Guid userId = Guid.NewGuid();
        Guid fullCourseId = Guid.NewGuid();
        Guid anotherFullCourseId = Guid.NewGuid();
        Guid trialCourseId = Guid.NewGuid();

        await writer.GrantManyAsync(
            userId,
            [
                GrantTags.AUTHENTICATED,
                GrantTags.Course(fullCourseId),
                GrantTags.Course(anotherFullCourseId),
                GrantTags.CourseTrial(fullCourseId),
                GrantTags.CourseTrial(trialCourseId),
            ]);

        EntitlementGrantSet entitlements = await reader.GetUserGrantTagsAsync(userId);

        Assert.Equal(5, entitlements.Tags.Count);
        Assert.Contains(GrantTags.AUTHENTICATED, entitlements.Tags);
        Assert.Contains(GrantTags.Course(fullCourseId), entitlements.Tags);
        Assert.Contains(GrantTags.Course(anotherFullCourseId), entitlements.Tags);
        Assert.Contains(GrantTags.CourseTrial(fullCourseId), entitlements.Tags);
        Assert.Contains(GrantTags.CourseTrial(trialCourseId), entitlements.Tags);
    }

    // ── E2E flow ───────────────────────────────────────────────────

    [Fact]
    public async Task E2E_WriterGrantsThenCheckerConfirms_ThenRevokeAndDeny()
    {
        var resourceWriter = new RedisResourceAccessWriter(_redis);
        var grantWriter = new RedisUserGrantWriter(_redis);
        var checker = new RedisEntitlementChecker(_redis);

        Guid resourceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        string tag = GrantTags.Course(Guid.NewGuid());

        await resourceWriter.SetTagsAsync("material", resourceId, [tag]);
        await grantWriter.GrantAsync(userId, tag);

        var user = new AccessSubject(true, userId, false);

        // Granted
        AccessDecision granted = await checker.CheckAccessAsync(user, "material", resourceId);
        Assert.True(granted.IsGranted);
        Assert.Equal(AccessReason.ENTITLEMENT, granted.Reason);

        // Revoke → Denied
        await grantWriter.RevokeAsync(userId, tag);

        AccessDecision denied = await checker.CheckAccessAsync(user, "material", resourceId);
        Assert.False(denied.IsGranted);
    }
}
