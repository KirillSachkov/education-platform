using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Projects;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;
using StackExchange.Redis;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class ProjectTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public ProjectTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    // ── GET /projects/{projectId}/detail ──────────────────────────────────

    [Fact]
    public async Task GetProjectDetail_ExistingProjectWithIssues_ReturnsDetailWithItems()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Detail Project", "Project Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 2, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/detail", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        Envelope<ProjectDetailDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<ProjectDetailDto>>(ct);
        Assert.NotNull(envelope);
        ProjectDetailDto dto = envelope.Result!;
        Assert.Equal(projectId, dto.Id);
        Assert.Equal("Detail Project", dto.Title);
        Assert.Equal("Project Desc", dto.Description);
        Assert.Equal("DRAFT", dto.Status);
        Assert.Equal(2, dto.Items.Count);

        // Verify issue IDs match
        Assert.Equal(issueIds[0], dto.Items[0].IssueId);
        Assert.Equal(issueIds[1], dto.Items[1].IssueId);
    }

    [Fact]
    public async Task GetProjectDetail_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/projects/{Guid.NewGuid()}/detail", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PATCH /projects/{projectId} ───────────────────────────────────────

    [Fact]
    public async Task UpdateProject_ValidRequest_UpdatesTitleAndDescription()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Original", "Original Desc", ct);
        var request = new UpdateProjectRequest("Updated Title", "Updated Desc");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{projectId}", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Project project = await db.Projects.FirstAsync(p => p.Id == projectId, ct);
            Assert.Equal("Updated Title", project.Title.Value);
            Assert.Equal("Updated Desc", project.Description!.Value);
        });
    }

    [Fact]
    public async Task UpdateProject_WithDetailedDescription_PersistsAndReturnsViaDetail()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Original", "Original Desc", ct);
        const string detailed = "## Бриф\n\nПостроить API сервиса.";
        var request = new UpdateProjectRequest("Updated Title", "Updated Desc", detailed);

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{projectId}", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        HttpResponseMessage detailResponse = await AppHttpClient.GetAsync(
            $"/projects/{projectId}/detail", ct);
        detailResponse.EnsureSuccessStatusCode();
        Envelope<ProjectDetailDto>? envelope =
            await detailResponse.Content.ReadFromJsonAsync<Envelope<ProjectDetailDto>>(ct);
        ProjectDetailDto dto = envelope!.Result!;
        Assert.Equal("Updated Title", dto.Title);
        Assert.Equal("Updated Desc", dto.Description);
        Assert.Equal(detailed, dto.DetailedDescription);
    }

    [Fact]
    public async Task UpdateProject_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new UpdateProjectRequest("Title", "Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{Guid.NewGuid()}", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /projects/{projectId}/issues ─────────────────────────────────

    [Fact]
    public async Task CreateIssue_ValidRequest_CreatesIssueAndProjectItem()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        var request = new CreateProjectIssueRequest("Text Issue", "Issue content here");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/projects/{projectId}/issues", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Issue? issue = await db.Issues.FirstOrDefaultAsync(ct);
            Assert.NotNull(issue);
            Assert.Equal("Text Issue", issue.Title.Value);
            Assert.Equal("Issue content here", issue.Content.Value);
            Assert.Equal(projectId, issue.ProjectId);

            ProjectItem? item = await db.ProjectItems
                .FirstOrDefaultAsync(i => i.ProjectId == projectId && i.IssueId == issue.Id, ct);
            Assert.NotNull(item);
            Assert.False(item.IsOptional);
            Assert.Null(item.MaxScore);
        });
    }

    [Fact]
    public async Task CreateIssue_SelfCheckRequest_PersistsSubmissionModeAndInstructions()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Self Check Project", "Desc", ct);
        var request = new CreateProjectIssueRequest(
            "Self Check Issue",
            "Issue content here",
            "SELF_CHECK",
            "Запустите тесты и проверьте сценарий вручную.");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/projects/{projectId}/issues", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Issue issue = await db.Issues.SingleAsync(ct);
            Assert.Equal(IssueSubmissionMode.SELF_CHECK, issue.SubmissionMode);
            Assert.Equal("Запустите тесты и проверьте сценарий вручную.", issue.SelfCheckInstructions);
        });
    }

    [Fact]
    public async Task CreateIssue_EmptyContent_ReturnsBadRequest()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        var request = new CreateProjectIssueRequest("Issue", "");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/projects/{projectId}/issues", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateIssue_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new CreateProjectIssueRequest("Issue", "Content");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/projects/{Guid.NewGuid()}/issues", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateIssue_ContentPreservesMarkdownFormatting()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        const string markdownContent = """
                                       # Heading

                                       - item 1
                                       - item 2

                                       ```csharp
                                       var x = 1;
                                       ```
                                       """;
        var request = new CreateProjectIssueRequest("Markdown Issue", markdownContent);

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/projects/{projectId}/issues", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Issue? issue = await db.Issues.FirstOrDefaultAsync(ct);
            Assert.NotNull(issue);
            // MarkdownContent trims but preserves internal formatting
            Assert.Contains("# Heading", issue.Content.Value, StringComparison.Ordinal);
            Assert.Contains("```csharp", issue.Content.Value, StringComparison.Ordinal);
            Assert.Contains("var x = 1;", issue.Content.Value, StringComparison.Ordinal);
        });
    }

    // ── PATCH /projects/{projectId}/issues/{issueId}/move ─────────────────

    [Fact]
    public async Task MoveIssue_MoveToFirst_OrderChanges()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 3, ct);

        (Guid IssueId, string SortKey)[] items = await GetProjectItemsOrdered(projectId, ct);

        // Act — move last issue to first position
        var request = new MoveProjectIssueRequest(AfterSortKey: null, BeforeSortKey: items[0].SortKey);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{projectId}/issues/{items[2].IssueId}/move", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        (Guid IssueId, string SortKey)[] newItems = await GetProjectItemsOrdered(projectId, ct);
        Assert.Equal(items[2].IssueId, newItems[0].IssueId);
        Assert.Equal(items[0].IssueId, newItems[1].IssueId);
        Assert.Equal(items[1].IssueId, newItems[2].IssueId);
    }

    [Fact]
    public async Task MoveIssue_BothSortKeysNull_ReturnsBadRequest()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 2, ct);
        (Guid IssueId, string SortKey)[] items = await GetProjectItemsOrdered(projectId, ct);

        // Act
        var request = new MoveProjectIssueRequest(AfterSortKey: null, BeforeSortKey: null);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{projectId}/issues/{items[0].IssueId}/move", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveIssue_IssueBelongsToDifferentProject_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId1 = await CreateProjectInDb("Project 1", "Desc 1", ct);
        Guid projectId2 = await CreateProjectInDb("Project 2", "Desc 2", ct);
        Guid[] issuesP1 = await CreateIssuesViaApi(projectId1, 1, ct);
        Guid[] issuesP2 = await CreateIssuesViaApi(projectId2, 1, ct);
        (Guid IssueId, string SortKey)[] itemsP2 = await GetProjectItemsOrdered(projectId2, ct);

        // Act — try to move issue from project1 under project2's endpoint
        var request = new MoveProjectIssueRequest(AfterSortKey: itemsP2[0].SortKey, BeforeSortKey: null);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/projects/{projectId2}/issues/{issuesP1[0]}/move", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /projects/{projectId}/issues/{issueId} ─────────────────────

    [Fact]
    public async Task DetachIssue_ExistingIssue_RemovesProjectItemKeepsIssue()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 1, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/projects/{projectId}/issues/{issueIds[0]}", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            bool itemExists = await db.ProjectItems
                .AnyAsync(i => i.ProjectId == projectId && i.IssueId == issueIds[0], ct);
            Assert.False(itemExists);

            bool issueExists = await db.Issues.AnyAsync(i => i.Id == issueIds[0], ct);
            Assert.True(issueExists);
        });
    }

    [Fact]
    public async Task DetachIssue_NonExistentIssue_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/projects/{projectId}/issues/{Guid.NewGuid()}", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PATCH /issues/{issueId} ───────────────────────────────────────────

    [Fact]
    public async Task UpdateIssue_ValidRequest_UpdatesTitleAndContent()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 1, ct);

        var request = new UpdateIssueRequest("Updated Issue", "Updated content", "ENROLLED");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/issues/{issueIds[0]}", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Issue? issue = await db.Issues.FirstAsync(i => i.Id == issueIds[0], ct);
            Assert.Equal("Updated Issue", issue.Title.Value);
            Assert.Equal("Updated content", issue.Content.Value);
        });
    }

    [Fact]
    public async Task UpdateIssue_CanChangeSubmissionModeToSelfCheck()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Project", "Desc", ct);
        Guid[] issueIds = await CreateIssuesViaApi(projectId, 1, ct);
        _factory.OutboxCollector.Clear();

        var request = new UpdateIssueRequest(
            "Updated Issue",
            "Updated content",
            "PUBLIC",
            "SELF_CHECK",
            "Проверьте результат по чеклисту.");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/issues/{issueIds[0]}", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        IssueAccessChanged accessChanged =
            Assert.Single(_factory.OutboxCollector.OfType<IssueAccessChanged>());
        Assert.Equal(issueIds[0], accessChanged.IssueId);

        _factory.RedisDatabase.ClearReceivedCalls();
        await InvokeMessageAndWaitAsync(accessChanged);
        await _factory.RedisDatabase.Received(1).SetAddAsync(
            $"resource-access:issue:{issueIds[0]:D}",
            Arg.Is<RedisValue[]>(values => values.Any(v => v.ToString() == "access:public")),
            Arg.Any<CommandFlags>());

        await ExecuteInDb(async db =>
        {
            Issue issue = await db.Issues.FirstAsync(i => i.Id == issueIds[0], ct);
            Assert.Equal(IssueSubmissionMode.SELF_CHECK, issue.SubmissionMode);
            Assert.Equal("Проверьте результат по чеклисту.", issue.SelfCheckInstructions);
        });
    }

    [Fact]
    public async Task UpdateIssue_NonExistentIssue_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new UpdateIssueRequest("Title", "Content", "ENROLLED");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/issues/{Guid.NewGuid()}", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteIssue_DurableClearThenStaleAccessSync_DoesNotResurrectTags()
    {
        CancellationToken ct = CancellationToken.None;
        Guid projectId = await CreateProjectInDb("Delete access", "Desc", ct);
        Guid issueId = Assert.Single(await CreateIssuesViaApi(projectId, 1, ct));
        _factory.OutboxCollector.Clear();
        _factory.RedisDatabase.ClearReceivedCalls();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/issues/{issueId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IssueHardDeleted deleted = Assert.Single(_factory.OutboxCollector.OfType<IssueHardDeleted>());
        await InvokeMessageAndWaitAsync(deleted);
        await _factory.RedisDatabase.Received(1).KeyDeleteAsync(
            $"resource-access:issue:{issueId:D}",
            Arg.Any<CommandFlags>());

        _factory.RedisDatabase.ClearReceivedCalls();
        await InvokeMessageAndWaitAsync(new IssueAccessChanged(issueId));
        await _factory.RedisDatabase.DidNotReceive().SetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateProjectInDb(string title, string description, CancellationToken ct)
    {
        Guid projectId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var project = new Project(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create(description).Value);

            db.Projects.Add(project);
            projectId = project.Id;
            await db.SaveChangesAsync(ct);
        });

        return projectId;
    }

    /// <summary>Creates issues via the API and returns their IDs.</summary>
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

    private async Task<(Guid IssueId, string SortKey)[]> GetProjectItemsOrdered(
        Guid projectId, CancellationToken ct)
    {
        ProjectItem[] items = [];

        await ExecuteInDb(async db =>
        {
            items = await db.ProjectItems
                .Where(i => i.ProjectId == projectId)
                .OrderBy(i => i.SortKey)
                .ToArrayAsync(ct);
        });

        return items.Select(i => (i.IssueId, i.SortKey.Value)).ToArray();
    }
}
