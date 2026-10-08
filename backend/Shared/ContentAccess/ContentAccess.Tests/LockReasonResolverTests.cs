namespace ContentAccess.Tests;

public sealed class LockReasonResolverTests
{
    private static readonly Guid CourseA = Guid.NewGuid();
    private static readonly Guid CourseB = Guid.NewGuid();

    [Fact]
    public void Public_Resource_IsAccessible_ToAnyone()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [],
            userGrants: EntitlementGrantSet.Empty,
            isAuthenticated: false);

        Assert.True(result.IsAccessible);
        Assert.Null(result.LockReason);
    }

    [Fact]
    public void Registered_Resource_IsInaccessible_ForAnonymous()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            userGrants: EntitlementGrantSet.Empty,
            isAuthenticated: false);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.ANONYMOUS, result.LockReason);
    }

    [Fact]
    public void Registered_Resource_IsAccessible_ForAuthenticated()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.AUTHENTICATED],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public void Free_Material_ForAnonymous_IsAnonymous()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.CourseTrial(CourseA)],
            userGrants: EntitlementGrantSet.Empty,
            isAuthenticated: false);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.ANONYMOUS, result.LockReason);
    }

    [Fact]
    public void Free_Material_ForAuthenticatedWithoutTrial_RequiresTrial()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.CourseTrial(CourseA)],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.TRIAL_REQUIRED, result.LockReason);
    }

    [Fact]
    public void Free_Material_ForTrialUser_IsAccessible()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.CourseTrial(CourseA)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.CourseTrial(CourseA)]),
            isAuthenticated: true);

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public void Enrolled_Material_ForTrialUser_RequiresStandard()
    {
        // Пользователь записан TRIAL на курс A; материал требует STANDARD-записи.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.CourseTrial(CourseA)]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.STANDARD_REQUIRED, result.LockReason);
    }

    [Fact]
    public void Enrolled_Material_ForUserWithoutAnyEnrollment_NotEnrolled()
    {
        // Пользователь авторизован, но записи на курс A нет ни TRIAL, ни STANDARD.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA)],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.NOT_ENROLLED, result.LockReason);
    }

    [Fact]
    public void PlanAll_Resource_ForAuthenticatedWithoutPlan_RequiresPlan()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.PlanAll()],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.PLAN_REQUIRED, result.LockReason);
    }

    [Fact]
    public void PlanAll_Resource_ForFullAccessUser_IsAccessible()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.PlanAll()],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED, GrantTags.PlanAll()]),
            isAuthenticated: true);

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public void Enrolled_Material_ForStandardUser_IsAccessible()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.Course(CourseA)]),
            isAuthenticated: true);

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public void Multiple_Required_Tags_AnyMatch_Grants()
    {
        // Материал привязан к курсам A и B — любой доступ к одному из них разблокирует.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA), GrantTags.Course(CourseB)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.Course(CourseB)]),
            isAuthenticated: true);

        Assert.True(result.IsAccessible);
    }

    [Fact]
    public void Multiple_Required_Tags_NoMatch_NoTrialMatch_NotEnrolled()
    {
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA), GrantTags.Course(CourseB)],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.NOT_ENROLLED, result.LockReason);
    }

    [Fact]
    public void Multiple_Required_Tags_TrialForOneCourse_StandardRequired()
    {
        // Материал требует STANDARD на курс A или B; есть TRIAL только на A.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.Course(CourseA), GrantTags.Course(CourseB)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.CourseTrial(CourseA)]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.STANDARD_REQUIRED, result.LockReason);
    }

    [Fact]
    public void Mixed_Trial_And_Standard_Tags_NoGrants_Prefers_Trial_Cta()
    {
        // Материал FREE в курсе A (course:A:trial) и ENROLLED в курсе B (course:B).
        // Пользователь авторизован, но ничего не купил. CTA должно предложить trial на A —
        // это самый дешёвый путь открыть материал.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.CourseTrial(CourseA), GrantTags.Course(CourseB)],
            userGrants: new EntitlementGrantSet([GrantTags.AUTHENTICATED]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.TRIAL_REQUIRED, result.LockReason);
    }

    [Fact]
    public void Mixed_Trial_And_Standard_Tags_HasTrialForStandardCourse_Still_Standard()
    {
        // Материал FREE в курсе A (course:A:trial) и ENROLLED в курсе B (course:B).
        // У пользователя есть trial на B — значит он уже «знает» курс B, и всё что его
        // отделяет от материала — это upgrade до STANDARD.
        AccessLockResult result = LockReasonResolver.Resolve(
            requiredAccessTags: [GrantTags.CourseTrial(CourseA), GrantTags.Course(CourseB)],
            userGrants: new EntitlementGrantSet(
                [GrantTags.AUTHENTICATED, GrantTags.CourseTrial(CourseB)]),
            isAuthenticated: true);

        Assert.False(result.IsAccessible);
        Assert.Equal(LockReasons.STANDARD_REQUIRED, result.LockReason);
    }
}
