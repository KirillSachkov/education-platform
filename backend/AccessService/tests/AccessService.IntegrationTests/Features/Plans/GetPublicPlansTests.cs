using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetPublicPlansTests : AccessServiceTestsBase
{
    public GetPublicPlansTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(
        string slug,
        string displayName = "Полный доступ",
        string tier = nameof(PlanTier.FULL_ALL))
    {
        CreatePlanRequest createRequest = new(
            Tier: tier,
            Slug: slug,
            DisplayName: displayName,
            ShortDescription: "Доступ ко всему",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    private async Task<Guid> CreatePlanCourseAsync(
        string slug,
        string displayName)
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: slug,
            DisplayName: displayName,
            ShortDescription: "Курс",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 50_000_00,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    private async Task PublishAsync(Guid planId)
    {
        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
    }

    private async Task ArchiveAsync(Guid planId)
    {
        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Anonymous_caller_gets_only_public_active_plans()
    {
        // FULL_ALL — singleton на платформу, плюс COURSE-план (draft) — три разных кейса
        // public/draft/archived. (FREE и LEARN_ALL deprecated; тесты используют только
        // живые tier'ы — FULL_ALL и COURSE.)
        Guid publishedPlanId = await CreatePlanAsync(
            "plan-published",
            "Опубликованный",
            tier: nameof(PlanTier.FULL_ALL));
        await PublishAsync(publishedPlanId);

        // Plan 2 — draft COURSE, без publish'а.
        await CreatePlanCourseAsync("plan-draft", "Черновик");

        // Plan 3 — COURSE-план, published затем archived.
        Guid archivedPlanId = await CreatePlanCourseAsync(
            "plan-archived",
            "Архивный");
        await PublishAsync(archivedPlanId);
        await ArchiveAsync(archivedPlanId);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(publishedPlanId, envelope.Result[0].Id);
    }

    [Fact]
    public async Task Returns_empty_when_no_public_plans()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task Returns_all_public_platform_plans()
    {
        // Author A (default) creates and publishes a course plan.
        Guid authorAUserId = CurrentUserId;
        Guid authorAPlanId = await CreatePlanCourseAsync("plan-author-a", "Автор A план");
        await PublishAsync(authorAPlanId);

        // Switch to author B and publish another course plan. Public catalog is platform-wide.
        Guid authorBUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", authorBUserId);
        Guid authorBPlanId = await CreatePlanCourseAsync("plan-author-b", "Автор B план");
        await PublishAsync(authorBPlanId);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.Contains(envelope.Result, p => p.Id == authorAPlanId && p.AuthorId == authorAUserId);
        Assert.Contains(envelope.Result, p => p.Id == authorBPlanId && p.AuthorId == authorBUserId);
    }

    [Fact]
    public async Task GetPlanBySlug_returns_published_plan()
    {
        Guid authorId = CurrentUserId;
        Guid planId = await CreatePlanAsync("free", "Бесплатный");
        await PublishAsync(planId);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/by-slug/free");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PublicPlanDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PublicPlanDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Equal(planId, envelope.Result.Id);
        Assert.Equal("free", envelope.Result.Slug);
        Assert.Equal(authorId, envelope.Result.AuthorId);
    }

    [Fact]
    public async Task GetPlanBySlug_returns_404_for_unpublished_plan()
    {
        await CreatePlanAsync("hidden", "Скрытый");
        // Intentionally not publishing.

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/by-slug/hidden");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "plan.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task COURSE_plan_returns_IncludedCourses_with_title_from_ECS()
    {
        // Seed ECS fake with course title. AccessService handler batch-fetches via
        // GetCourseTitlesAsync; happy-path coverage of IncludedCourses enrichment.
        Factory.EduClient.CourseTitlesById.Clear();
        Guid courseAId = Guid.NewGuid();
        Factory.EduClient.CourseTitlesById[courseAId] = "Курс A";

        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "single-course",
            DisplayName: "Один курс",
            ShortDescription: "Курс автора",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 50_000_00,
            Currency: "RUB",
            CourseIds: courseAId is { } __cc ? [__cc] : [],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? created = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(created);
        await PublishAsync(created.Result);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        PublicPlanDto plan = Assert.Single(envelope.Result);
        Assert.Equal("COURSE", plan.Tier);
        Assert.NotNull(plan.IncludedCourses);
        PublicPlanCourseDto course = Assert.Single(plan.IncludedCourses!);
        Assert.Equal(courseAId, course.Id);
        Assert.Equal("Курс A", course.Title);
    }

    [Fact]
    public async Task TRAINER_PRO_SUBSCRIPTION_plan_is_excluded_from_public_catalog() // #674 — supersedes #614
    {
        // A persisted historical trainer offer remains hidden from the platform catalog.
        Guid platformPlanId = await CreatePlanAsync("platform-full", "Полный доступ");
        await PublishAsync(platformPlanId);

        Guid trainerPlanId = await SeedLegacyTrainerPlanAsync("trainer-pro");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        // Only the PLATFORM plan comes back — the TRAINER_PRO subscription is filtered out.
        PublicPlanDto plan = Assert.Single(envelope.Result!);
        Assert.Equal(platformPlanId, plan.Id);
        Assert.DoesNotContain(envelope.Result!, p => p.Id == trainerPlanId);
    }

    [Fact]
    public async Task GetPlanBySlug_returns_404_for_TRAINER_scoped_plan() // #674
    {
        // The trainer subscription has a slug but must not be reachable through the public
        // platform pricing detail page (/access/plans/by-slug/{slug}).
        await SeedLegacyTrainerPlanAsync("trainer-pro-detail");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/by-slug/trainer-pro-detail");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bundle_COURSE_plan_returns_IncludedCourses_for_all_courses() // #404 success criterion
    {
        // A COURSE plan bound to 2 courses must come back with IncludedCourses listing both.
        Factory.EduClient.CourseTitlesById.Clear();
        Guid courseAId = Guid.NewGuid();
        Guid courseBId = Guid.NewGuid();
        Factory.EduClient.CourseTitlesById[courseAId] = "Курс A";
        Factory.EduClient.CourseTitlesById[courseBId] = "Курс B";

        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "bundle-ab",
            DisplayName: "Бандл A+B",
            ShortDescription: "Доступ к двум курсам",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 80_000_00,
            Currency: "RUB",
            CourseIds: [courseAId, courseBId],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? created = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(created);
        await PublishAsync(created.Result);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        PublicPlanDto plan = Assert.Single(envelope.Result!);

        Assert.Equal("COURSE", plan.Tier);
        Assert.Equal(2, plan.CourseIds.Count);
        Assert.NotNull(plan.IncludedCourses);
        Assert.Equal(2, plan.IncludedCourses!.Count);
        Assert.Contains(plan.IncludedCourses!, c => c.Id == courseAId && c.Title == "Курс A");
        Assert.Contains(plan.IncludedCourses!, c => c.Id == courseBId && c.Title == "Курс B");
    }
    private async Task<Guid> SeedLegacyTrainerPlanAsync(string slug)
    {
        Plan plan = Plan.Create(CurrentUserId, PlanTier.SUBSCRIPTION, PlanSlug.Of(slug).Value,
            PlanDisplayName.Of("Legacy subscription").Value, [], null, term: PlanTerm.Recurring(30)).Value;
        plan.UpdatePrice(49_000, "RUB");
        plan.Publish();
        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan.Id;
    }

}