using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Catalog-visibility gate (#569, model A co-author). A PUBLISHED course created by a
///     non-admin author is hidden from the public catalog / by-author portfolio until an
///     admin/moderator approves it via <c>PATCH /courses/{id}/catalog-listing</c>. The author
///     still sees their own course in <c>/courses/my</c> (flagged <c>IsCatalogListed=false</c>).
///     Admin-created courses are listed immediately. Also covers author-credit enrichment in
///     the catalog DTO (mocked <see cref="IAuthorLookupClient"/>).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CatalogVisibilityGateTests : EducationContentServiceTestsBase
{
    public CatalogVisibilityGateTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task NonAdminAuthor_publishedCourse_isHiddenFromCatalogAndPortfolio_butVisibleInMyCourses()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string marker = Guid.NewGuid().ToString("N")[..8];

        AuthenticateAs(authorId, "platform-author");
        Guid courseId = await CreateAndPublishCourseAsync($"Gated {marker}", marker, ct);

        // Catalog — gated course is ABSENT.
        CursorResponse<CourseCatalogDto> catalog = await GetCatalogAsync(marker, ct);
        Assert.DoesNotContain(catalog.Items, c => c.Id == courseId);

        // By-author portfolio (anonymous) — also ABSENT.
        RemoveAuthentication();
        HttpResponseMessage portfolioResp = await AppHttpClient.GetAsync(
            $"/courses/by-author/{authorId}?limit=50", ct);
        Assert.Equal(HttpStatusCode.OK, portfolioResp.StatusCode);
        CursorResponse<CourseCatalogDto> portfolio =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(portfolioResp);
        Assert.DoesNotContain(portfolio.Items, c => c.Id == courseId);

        // /courses/my — author sees their own course, flagged IsCatalogListed=false.
        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage myResp = await AppHttpClient.GetAsync("/courses/my?limit=50", ct);
        Assert.Equal(HttpStatusCode.OK, myResp.StatusCode);
        CursorResponse<CourseSummaryDto> mine =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(myResp);
        CourseSummaryDto own = mine.Items.Single(c => c.Id == courseId);
        Assert.False(own.IsCatalogListed);
    }

    [Fact]
    public async Task Admin_approvesCatalogListing_courseAppearsInCatalog()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string marker = Guid.NewGuid().ToString("N")[..8];

        AuthenticateAs(authorId, "platform-author");
        Guid courseId = await CreateAndPublishCourseAsync($"Approve {marker}", marker, ct);

        // Before approval — absent.
        Assert.DoesNotContain((await GetCatalogAsync(marker, ct)).Items, c => c.Id == courseId);

        // Admin approves.
        AuthenticateAsAdmin();
        HttpResponseMessage patch = await PatchAsJsonAsync(
            $"/courses/{courseId}/catalog-listing", new SetCatalogListingRequest(true));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // After approval — present. Distinct marker keeps the 60s catalog cache isolated.
        RemoveAuthentication();
        CursorResponse<CourseCatalogDto> catalog = await GetCatalogAsync(marker, ct);
        Assert.Contains(catalog.Items, c => c.Id == courseId);
    }

    [Fact]
    public async Task AdminCreatedCourse_isCatalogListedImmediately_andVisibleInCatalog()
    {
        CancellationToken ct = CancellationToken.None;
        string marker = Guid.NewGuid().ToString("N")[..8];

        AuthenticateAsAdmin();
        Guid courseId = await CreateAndPublishCourseAsync($"AdminMade {marker}", marker, ct);

        // /courses/my (as the same admin) — flagged listed.
        HttpResponseMessage myResp = await AppHttpClient.GetAsync("/courses/my?limit=50", ct);
        CursorResponse<CourseSummaryDto> mine =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(myResp);
        Assert.True(mine.Items.Single(c => c.Id == courseId).IsCatalogListed);

        // Catalog — present immediately, no approval step.
        RemoveAuthentication();
        CursorResponse<CourseCatalogDto> catalog = await GetCatalogAsync(marker, ct);
        Assert.Contains(catalog.Items, c => c.Id == courseId);
    }

    [Fact]
    public async Task PendingListing_returnsUnapprovedCourse_enrichedWithAuthorName()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string marker = Guid.NewGuid().ToString("N")[..8];

        AuthenticateAs(authorId, "platform-author");
        Guid courseId = await CreateAndPublishCourseAsync($"Pending {marker}", marker, ct);

        // Mock author-credit so the moderation queue shows a known display name.
        IAuthorLookupClient client = Services.GetRequiredService<IAuthorLookupClient>();
        client.GetAuthorsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(
                new Dictionary<Guid, AuthorCreditDto>
                {
                    [authorId] = new AuthorCreditDto(authorId, "Иван Автор", AvatarId: null),
                }));

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/courses/admin/pending-listing?limit=100", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        CursorResponse<PendingCatalogCourseDto> pending =
            await ReadResultAsync<CursorResponse<PendingCatalogCourseDto>>(resp);

        PendingCatalogCourseDto item = pending.Items.Single(c => c.Id == courseId);
        Assert.Equal(authorId, item.AuthorId);
        Assert.Equal("Иван Автор", item.AuthorDisplayName);
    }

    [Fact]
    public async Task PendingListing_requiresContentModerate_403ForAuthorAndParticipant()
    {
        CancellationToken ct = CancellationToken.None;

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage authorResp = await AppHttpClient.GetAsync("/courses/admin/pending-listing", ct);
        Assert.Equal(HttpStatusCode.Forbidden, authorResp.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        HttpResponseMessage participantResp = await AppHttpClient.GetAsync("/courses/admin/pending-listing", ct);
        Assert.Equal(HttpStatusCode.Forbidden, participantResp.StatusCode);
    }

    [Fact]
    public async Task CatalogListingPatch_requiresContentModerate_403ForAuthor()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string marker = Guid.NewGuid().ToString("N")[..8];

        AuthenticateAs(authorId, "platform-author");
        Guid courseId = await CreateAndPublishCourseAsync($"NoSelfApprove {marker}", marker, ct);

        // Same author (no Content.MODERATE) cannot self-approve.
        HttpResponseMessage patch = await PatchAsJsonAsync(
            $"/courses/{courseId}/catalog-listing", new SetCatalogListingRequest(true));
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    [Fact]
    public async Task Catalog_dto_carriesAuthorDisplayName()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string marker = Guid.NewGuid().ToString("N")[..8];

        // Admin-created → listed immediately, so it shows up in the public catalog.
        AuthenticateAsAdmin(authorId);
        Guid courseId = await CreateAndPublishCourseAsync($"Credited {marker}", marker, ct);

        IAuthorLookupClient client = Services.GetRequiredService<IAuthorLookupClient>();
        client.GetAuthorsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error>(
                new Dictionary<Guid, AuthorCreditDto>
                {
                    [authorId] = new AuthorCreditDto(authorId, "Пётр Преподаватель", AvatarId: null),
                }));

        RemoveAuthentication();
        CursorResponse<CourseCatalogDto> catalog = await GetCatalogAsync(marker, ct);
        CourseCatalogDto card = catalog.Items.Single(c => c.Id == courseId);
        Assert.Equal("Пётр Преподаватель", card.AuthorDisplayName);
    }

    private async Task<Guid> CreateAndPublishCourseAsync(string title, string slugMarker, CancellationToken ct)
    {
        var createReq = new
        {
            Title = title,
            Description = $"{title} description",
            Slug = $"course-{slugMarker}",
        };

        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync("/courses", createReq, ct);
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        Guid courseId = await ReadResultAsync<Guid>(createResp);

        HttpResponseMessage publishResp = await AppHttpClient.PostAsync($"/courses/{courseId}/publish", null, ct);
        Assert.Equal(HttpStatusCode.OK, publishResp.StatusCode);

        return courseId;
    }

    private async Task<CursorResponse<CourseCatalogDto>> GetCatalogAsync(string searchMarker, CancellationToken ct)
    {
        // Unique search marker per test keeps the 60s catalog cache key isolated.
        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/courses/catalog?limit=50&search={searchMarker}", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<CursorResponse<CourseCatalogDto>>(resp);
    }
}
