using System.Net;
using System.Net.Http.Json;
using AccessService.Core.Database;
using AccessService.Core.Features.Plans.UseCases;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// L2 integration tests for <c>POST /internal/access/plans/by-course-ids</c>.
/// Verifies S2S auth gate, validation, and filter semantics
/// (tier=COURSE + IsActive + IsPublic + ArchivedAt IS NULL).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetPlansByCourseIdsTests : AccessServiceTestsBase
{
    public GetPlansByCourseIdsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Returns_only_active_public_non_archived_COURSE_plans()
    {
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        Guid courseActivePublic = Guid.NewGuid();
        Guid courseInactive = Guid.NewGuid();
        Guid coursePrivate = Guid.NewGuid();
        Guid courseFullAll = Guid.NewGuid();   // sentinel — no plan binds to this id

        Guid expectedPlanId = await SeedPlanAsync(authorA, PlanTier.COURSE, courseActivePublic,
            "course-active-public", "Активный курс", priceCents: 1_200_00,
            publish: true);

        await SeedPlanAsync(authorA, PlanTier.COURSE, courseInactive,
            "course-inactive", "Архивный курс", priceCents: 500_00,
            publish: true, archive: true);

        await SeedPlanAsync(authorA, PlanTier.COURSE, coursePrivate,
            "course-private", "Приватный курс", priceCents: 700_00,
            publish: false);

        // FULL_ALL plan with no CourseId — must not match any query courseId.
        await SeedPlanAsync(authorB, PlanTier.FULL_ALL, courseId: null,
            "full-all", "Полный доступ", priceCents: 990_00,
            publish: true);

        AuthenticateAs("platform-service", Guid.NewGuid());

        GetPlansByCourseIdsRequest request = new(
            CourseIds: [courseActivePublic, courseInactive, coursePrivate, courseFullAll]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetPlansByCourseIdsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetPlansByCourseIdsResponse>>();
        Assert.NotNull(envelope?.Result);
        Assert.False(envelope!.IsError);

        PlanByCourseDto plan = Assert.Single(envelope.Result!.Plans);
        Assert.Equal(expectedPlanId, plan.PlanId);
        Assert.Equal(courseActivePublic, plan.CourseId);
        Assert.Equal(authorA, plan.AuthorId);
        Assert.Equal(nameof(PlanTier.COURSE), plan.Tier);
        Assert.Equal("course-active-public", plan.Slug);
        Assert.Equal("Активный курс", plan.DisplayName);
        Assert.Equal(1_200_00, plan.PriceCents);
        Assert.Equal("RUB", plan.Currency);
        Assert.True(plan.IsActive);
        Assert.True(plan.IsPublic);
    }

    [Fact]
    public async Task Empty_courseIds_returns_empty_plans_array()
    {
        AuthenticateAs("platform-service", Guid.NewGuid());

        GetPlansByCourseIdsRequest request = new(CourseIds: []);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetPlansByCourseIdsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetPlansByCourseIdsResponse>>();
        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope!.Result!.Plans);
    }

    [Fact]
    public async Task Unauthorized_without_jwt_returns_401()
    {
        RemoveAuthentication();

        GetPlansByCourseIdsRequest request = new(CourseIds: [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Author_role_cannot_call_internal_endpoint()
    {
        // Default test identity is platform-author — must be rejected with 403.
        GetPlansByCourseIdsRequest request = new(CourseIds: [Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Too_many_ids_returns_400_with_validation_code()
    {
        AuthenticateAs("platform-service", Guid.NewGuid());

        Guid[] ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();
        GetPlansByCourseIdsRequest request = new(CourseIds: ids);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope!.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "plans.by_course.too_many", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bundle_plan_emits_one_row_per_matched_course() // #404 — multi-course bundle
    {
        Guid authorId = Guid.NewGuid();
        Guid courseA = Guid.NewGuid();
        Guid courseB = Guid.NewGuid();
        Guid courseC = Guid.NewGuid(); // not in the bundle — must not appear

        Guid bundleId = await SeedBundleAsync(authorId, [courseA, courseB],
            "bundle-ab", "Бандл A+B", priceCents: 2_000_00, publish: true);

        AuthenticateAs("platform-service", Guid.NewGuid());

        GetPlansByCourseIdsRequest request = new(CourseIds: [courseA, courseB, courseC]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/plans/by-course-ids", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GetPlansByCourseIdsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetPlansByCourseIdsResponse>>();
        Assert.NotNull(envelope?.Result);

        // One row per (plan, matched course) — A and B only, never C.
        IReadOnlyList<PlanByCourseDto> rows = envelope!.Result!.Plans;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(bundleId, r.PlanId));
        Assert.Contains(rows, r => r.CourseId == courseA);
        Assert.Contains(rows, r => r.CourseId == courseB);
        Assert.DoesNotContain(rows, r => r.CourseId == courseC);
    }

    private async Task<Guid> SeedBundleAsync(
        Guid authorId,
        IReadOnlyList<Guid> courseIds,
        string slug,
        string displayName,
        long priceCents,
        bool publish)
    {
        Result<Plan, Error> create = Plan.Create(
            authorId: authorId,
            tier: PlanTier.COURSE,
            slug: PlanSlug.Of(slug).Value,
            displayName: PlanDisplayName.Of(displayName).Value,
            courseIds: courseIds,
            requestedCapabilities: null);
        Assert.True(create.IsSuccess, create.IsFailure ? create.Error.Messages[0].Code : null);

        Plan plan = create.Value;
        plan.UpdatePrice(priceCents, "RUB");
        if (publish)
        {
            plan.Publish();
        }

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IPlansRepository plans = scope.ServiceProvider.GetRequiredService<IPlansRepository>();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        await plans.AddAsync(plan);
        UnitResult<Error> save = await transactions.SaveChangesAsync();
        Assert.True(save.IsSuccess, save.IsFailure ? save.Error.Messages[0].Code : null);

        return plan.Id;
    }

    /// <summary>
    /// Seeds a Plan directly via repository — bypasses the create-endpoint (so we can
    /// vary author per row + archive in-place without going through unrelated handlers).
    /// </summary>
    private async Task<Guid> SeedPlanAsync(
        Guid authorId,
        PlanTier tier,
        Guid? courseId,
        string slug,
        string displayName,
        long priceCents,
        bool publish,
        bool archive = false)
    {
        Result<Plan, Error> create = Plan.Create(
            authorId: authorId,
            tier: tier,
            slug: PlanSlug.Of(slug).Value,
            displayName: PlanDisplayName.Of(displayName).Value,
            courseIds: courseId is { } __cc ? [__cc] : [],
            requestedCapabilities: null);
        Assert.True(create.IsSuccess, create.IsFailure ? create.Error.Messages[0].Code : null);

        Plan plan = create.Value;
        plan.UpdatePrice(priceCents, "RUB");
        if (publish)
        {
            plan.Publish();
        }
        if (archive)
        {
            UnitResult<Error> archiveResult = plan.Archive();
            Assert.True(archiveResult.IsSuccess);
        }

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IPlansRepository plans = scope.ServiceProvider.GetRequiredService<IPlansRepository>();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        await plans.AddAsync(plan);
        UnitResult<Error> save = await transactions.SaveChangesAsync();
        Assert.True(save.IsSuccess, save.IsFailure ? save.Error.Messages[0].Code : null);

        return plan.Id;
    }
}
