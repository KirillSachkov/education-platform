using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Projects;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class ProjectReviewCoverageTests : EducationContentServiceTestsBase
{
    public ProjectReviewCoverageTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetReviewCoverage_MixedSpecs_ReturnsPerIssueStatus()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Coverage Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 2, ct);

        const string guidelines = "Project-level review guidelines markdown.";
        const string authorPrompt = "Check the repository pattern usage.";
        const string aspects = "Focus on error handling.";

        await ExecuteInDb(async db =>
        {
            db.ProjectReviewContexts.Add(
                ProjectReviewContext.Create(projectId, guidelines, isAutoReviewEnabled: true));
            // issue[0] gets a spec with auto-review explicitly disabled to prove the flag flows.
            db.ReviewSpecs.Add(
                ReviewSpec.Create(issueIds[0], projectId, authorPrompt, aspects, isAutoReviewEnabled: false));
            await db.SaveChangesAsync(ct);
        });

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/review-coverage", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        Envelope<ProjectReviewCoverageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ProjectReviewCoverageDto>>(ct);
        Assert.NotNull(envelope);
        ProjectReviewCoverageDto dto = envelope.Result!;

        Assert.Equal(projectId, dto.ProjectId);
        Assert.Equal("Coverage Project", dto.ProjectTitle);
        Assert.True(dto.HasProjectContext);
        Assert.Equal(guidelines.Length, dto.GuidelinesLength);
        Assert.Equal(2, dto.Issues.Count);

        IssueReviewCoverageDto withSpec = dto.Issues.Single(i => i.IssueId == issueIds[0]);
        Assert.True(withSpec.HasReviewSpec);
        Assert.Equal(authorPrompt.Length, withSpec.AuthorPromptLength);
        Assert.Equal(aspects.Length, withSpec.ReviewAspectsLength);
        Assert.False(withSpec.IsAutoReviewEnabled);
        Assert.NotNull(withSpec.ReviewSpecUpdatedAt);

        IssueReviewCoverageDto withoutSpec = dto.Issues.Single(i => i.IssueId == issueIds[1]);
        Assert.False(withoutSpec.HasReviewSpec);
        Assert.Equal(0, withoutSpec.AuthorPromptLength);
        Assert.Equal(0, withoutSpec.ReviewAspectsLength);
        Assert.True(withoutSpec.IsAutoReviewEnabled); // effective default when no spec
        Assert.Null(withoutSpec.ReviewSpecUpdatedAt);
    }

    [Fact]
    public async Task GetReviewCoverage_NoContextNoSpecs_ReportsEmpty()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Empty Project", "Desc", ct);
        await CreateIssuesViaApi(projectId, 1, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/review-coverage", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        Envelope<ProjectReviewCoverageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ProjectReviewCoverageDto>>(ct);
        ProjectReviewCoverageDto dto = envelope!.Result!;

        Assert.False(dto.HasProjectContext);
        Assert.Equal(0, dto.GuidelinesLength);
        Assert.True(dto.ProjectIsAutoReviewEnabled); // effective default
        Assert.Null(dto.ProjectContextUpdatedAt);
        Assert.Single(dto.Issues);
        Assert.False(dto.Issues[0].HasReviewSpec);
    }

    [Fact]
    public async Task GetReviewCoverage_NonExistentProject_ReturnsNotFound()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{Guid.NewGuid()}/review-coverage", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReviewCoverage_ProjectOwnerNonAdmin_Succeeds()
    {
        // Arrange — caller is the project's own author (non-admin, has Issues.MANAGE).
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid projectId = await CreateProjectInDb("Owner Project", "Desc", ct, authorId);

        AuthenticateAs(authorId, "platform-author");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/review-coverage", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        Envelope<ProjectReviewCoverageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ProjectReviewCoverageDto>>(ct);
        Assert.Equal(projectId, envelope!.Result!.ProjectId);
    }

    [Fact]
    public async Task GetReviewCoverage_NonOwnerNonAdmin_ReturnsForbidden()
    {
        // Arrange — project author is a random guid (≠ the caller below).
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Owned Project", "Desc", ct);

        // An author who has Issues.MANAGE but does not own this project.
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/review-coverage", ct);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateProjectInDb(
        string title, string description, CancellationToken ct, Guid? authorId = null)
    {
        Guid projectId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var project = new Project(
                authorId ?? Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create(description).Value);

            db.Projects.Add(project);
            projectId = project.Id;
            await db.SaveChangesAsync(ct);
        });

        return projectId;
    }

    private async Task<Guid[]> CreateIssuesViaApi(Guid projectId, int count, CancellationToken ct)
    {
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];
        List<Guid> issueIds = [];

        for (int i = 0; i < count; i++)
        {
            var request = new CreateProjectIssueRequest(
                $"Issue {uniquePrefix} {i + 1}",
                $"Issue content {i + 1}");

            HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
                $"/projects/{projectId}/issues", request, ct);
            response.EnsureSuccessStatusCode();

            Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>(ct);
            issueIds.Add(envelope!.Result);
        }

        return issueIds.ToArray();
    }
}
