using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Roadmaps;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Roadmaps;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class RoadmapTests : EducationContentServiceTestsBase
{
    public RoadmapTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateRoadmap_StandaloneWithSlug_ReturnsId()
    {
        // Arrange
        var request = new CreateRoadmapRequest("Python Roadmap", "Full path", null, "python-roadmap");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Guid roadmapId = await ReadResultAsync<Guid>(response);
        Assert.NotEqual(Guid.Empty, roadmapId);

        await ExecuteInDb(async db =>
        {
            Roadmap? roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId);
            Assert.NotNull(roadmap);
            Assert.Equal("Python Roadmap", roadmap.Title.Value);
            Assert.Null(roadmap.CourseId);
            Assert.Equal("python-roadmap", roadmap.Slug);
            Assert.Equal(PublicationStatus.DRAFT, roadmap.Status);
        });
    }

    [Fact]
    public async Task CreateRoadmap_CourseBound_ReturnsId()
    {
        // Arrange
        Guid courseId = await CreateCourseInDb();
        var request = new CreateRoadmapRequest("Course Roadmap", null, courseId, null);

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Guid roadmapId = await ReadResultAsync<Guid>(response);

        await ExecuteInDb(async db =>
        {
            Roadmap? roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId);
            Assert.NotNull(roadmap);
            Assert.Equal(courseId, roadmap.CourseId);
        });
    }

    [Fact]
    public async Task CreateRoadmap_DuplicateCourseId_ReturnsConflict()
    {
        // Arrange
        Guid courseId = await CreateCourseInDb();
        await CreateRoadmapViaApi("First", courseId: courseId);
        var request = new CreateRoadmapRequest("Second", null, courseId, null);

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoadmap_DuplicateSlug_ReturnsConflict()
    {
        // Arrange
        await CreateRoadmapViaApi("First", slug: "my-slug");
        var request = new CreateRoadmapRequest("Second", null, null, "my-slug");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateRoadmap_Unauthorized_Returns401()
    {
        // Arrange
        RemoveAuthentication();
        var request = new CreateRoadmapRequest("Test", null, null, null);

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRoadmap_ValidData_UpdatesFields()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Original Title");
        var request = new UpdateRoadmapRequest("Updated Title", "New description", "new-slug");

        // Act
        HttpResponseMessage response = await AppHttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}")
            {
                Content = JsonContent.Create(request)
            });

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Roadmap? roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId);
            Assert.NotNull(roadmap);
            Assert.Equal("Updated Title", roadmap.Title.Value);
            Assert.Equal("New description", roadmap.Description!.Value);
            Assert.Equal("new-slug", roadmap.Slug);
        });
    }

    [Fact]
    public async Task SaveCanvas_NodesAndEdges_PersistsCorrectly()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Canvas Test");
        Guid nodeId1 = Guid.NewGuid();
        Guid nodeId2 = Guid.NewGuid();
        Guid edgeId = Guid.NewGuid();

        var request = new SaveCanvasRequest(
            Nodes:
            [
                new SaveCanvasNodeDto(nodeId1, "EntityReference", 100, 200, null, null, null,
                    """{"entityType":"Lesson","entityId":"00000000-0000-0000-0000-000000000001"}""", 0),
                new SaveCanvasNodeDto(nodeId2, "TextNote", 300, 400, null, null, null,
                    """{"text":"Start here"}""", 1)
            ],
            Edges:
            [
                new SaveCanvasEdgeDto(edgeId, nodeId1, nodeId2, "then", "smoothstep", false, null, null)
            ]);

        // Act
        HttpResponseMessage response = await AppHttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}/canvas")
            {
                Content = JsonContent.Create(request)
            });

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            List<RoadmapNode> nodes = await db.RoadmapNodes
                .Where(n => n.RoadmapId == roadmapId)
                .OrderBy(n => n.SortOrder)
                .ToListAsync();

            Assert.Equal(2, nodes.Count);
            Assert.Equal(nodeId1, nodes[0].Id);
            Assert.Equal(RoadmapNodeType.EntityReference, nodes[0].NodeType);
            Assert.Equal(100, nodes[0].PositionX);
            Assert.Equal(200, nodes[0].PositionY);

            Assert.Equal(nodeId2, nodes[1].Id);
            Assert.Equal(RoadmapNodeType.TextNote, nodes[1].NodeType);

            List<RoadmapEdge> edges = await db.RoadmapEdges
                .Where(e => e.RoadmapId == roadmapId)
                .ToListAsync();

            Assert.Single(edges);
            Assert.Equal(nodeId1, edges[0].SourceNodeId);
            Assert.Equal(nodeId2, edges[0].TargetNodeId);
            Assert.Equal("then", edges[0].Label);
            Assert.Equal("smoothstep", edges[0].EdgeType);
        });
    }

    [Fact]
    public async Task SaveCanvas_ReplacesExistingNodes()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Replace Test");
        Guid oldNodeId = Guid.NewGuid();

        // Save initial canvas
        var initial = new SaveCanvasRequest(
            Nodes: [new SaveCanvasNodeDto(oldNodeId, "TextNote", 0, 0, null, null, null, """{"text":"old"}""", 0)],
            Edges: []);
        await AppHttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}/canvas")
        {
            Content = JsonContent.Create(initial)
        });

        // Save replacement canvas
        Guid newNodeId = Guid.NewGuid();
        var replacement = new SaveCanvasRequest(
            Nodes: [new SaveCanvasNodeDto(newNodeId, "TextNote", 50, 50, null, null, null, """{"text":"new"}""", 0)],
            Edges: []);

        // Act
        HttpResponseMessage response = await AppHttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}/canvas")
            {
                Content = JsonContent.Create(replacement)
            });

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            List<RoadmapNode> nodes = await db.RoadmapNodes
                .Where(n => n.RoadmapId == roadmapId)
                .ToListAsync();

            Assert.Single(nodes);
            Assert.Equal(newNodeId, nodes[0].Id);
            Assert.Contains("new", nodes[0].Data, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SaveCanvas_WithGroupParent_PersistsParentNodeId()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Group Test");
        Guid groupId = Guid.NewGuid();
        Guid childId = Guid.NewGuid();

        var request = new SaveCanvasRequest(
            Nodes:
            [
                new SaveCanvasNodeDto(groupId, "Group", 0, 0, 400, 300, null, """{"label":"Basics"}""", 0),
                new SaveCanvasNodeDto(childId, "EntityReference", 50, 50, null, null, groupId,
                    """{"entityType":"Lesson","entityId":"00000000-0000-0000-0000-000000000001"}""", 1)
            ],
            Edges: []);

        // Act
        HttpResponseMessage response = await AppHttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}/canvas")
            {
                Content = JsonContent.Create(request)
            });

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            RoadmapNode? child = await db.RoadmapNodes.FirstOrDefaultAsync(n => n.Id == childId);
            Assert.NotNull(child);
            Assert.Equal(groupId, child.ParentNodeId);
        });
    }

    [Fact]
    public async Task GetRoadmap_ExistingRoadmap_ReturnsFullData()
    {
        // Arrange — create via API so authorId matches authenticated user
        Guid roadmapId = await CreateRoadmapViaApi("Get Test");
        Guid nodeId = Guid.NewGuid();

        await SaveCanvasViaApi(roadmapId, new SaveCanvasRequest(
            Nodes: [new SaveCanvasNodeDto(nodeId, "TextNote", 10, 20, null, null, null, """{"text":"hello"}""", 0)],
            Edges: []));

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/roadmaps/{roadmapId}");

        // Assert
        response.EnsureSuccessStatusCode();
        RoadmapDto dto = await ReadResultAsync<RoadmapDto>(response);

        Assert.Equal(roadmapId, dto.Id);
        Assert.Equal("Get Test", dto.Title);
        Assert.Single(dto.Nodes);
        Assert.Equal(nodeId, dto.Nodes[0].Id);
        Assert.Equal("TextNote", dto.Nodes[0].NodeType);
        Assert.Empty(dto.Edges);
    }

    [Fact]
    public async Task GetRoadmapByCourse_Exists_ReturnsRoadmap()
    {
        // Arrange — create via API so authorId matches
        Guid courseId = await CreateCourseInDb();
        Guid roadmapId = await CreateRoadmapViaApi("By Course Test", courseId: courseId);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/roadmaps/by-course/{courseId}");

        // Assert
        response.EnsureSuccessStatusCode();
        RoadmapDto? dto = await ReadResultAsync<RoadmapDto?>(response);
        Assert.NotNull(dto);
        Assert.Equal(roadmapId, dto.Id);
    }

    [Fact]
    public async Task GetRoadmapByCourse_NotExists_ReturnsNull()
    {
        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/roadmaps/by-course/{Guid.NewGuid()}");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetRoadmapBySlug_PublishedRoadmap_ReturnsData()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Slug Test", slug: "test-slug");

        // Publish it
        await AppHttpClient.PostAsync($"/roadmaps/{roadmapId}/publish", null);

        RemoveAuthentication(); // anonymous access

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/roadmaps/by-slug/test-slug");

        // Assert
        response.EnsureSuccessStatusCode();
        RoadmapDto dto = await ReadResultAsync<RoadmapDto>(response);
        Assert.Equal("test-slug", dto.Slug);
    }

    [Fact]
    public async Task GetRoadmapBySlug_DraftRoadmap_Returns404()
    {
        // Arrange
        await CreateRoadmapViaApi("Draft Slug", slug: "draft-slug");
        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/roadmaps/by-slug/draft-slug");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRoadmaps_ListsAll()
    {
        // Arrange
        await CreateRoadmapViaApi("Roadmap A");
        await CreateRoadmapViaApi("Roadmap B");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/roadmaps?limit=50");

        // Assert
        response.EnsureSuccessStatusCode();
        CursorResponse<RoadmapSummaryDto> page = await ReadResultAsync<CursorResponse<RoadmapSummaryDto>>(response);
        Assert.True(page.Items.Count >= 2);
    }

    [Fact]
    public async Task PublishRoadmap_DraftRoadmap_StatusBecomesPublished()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Publish Test");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsync($"/roadmaps/{roadmapId}/publish", null);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Roadmap? roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId);
            Assert.NotNull(roadmap);
            Assert.Equal(PublicationStatus.PUBLISHED, roadmap.Status);
        });
    }

    [Fact]
    public async Task DeleteRoadmap_Existing_CascadesChildren()
    {
        // Arrange
        Guid roadmapId = await CreateRoadmapViaApi("Delete Test");
        Guid nodeId = Guid.NewGuid();
        await SaveCanvasViaApi(roadmapId, new SaveCanvasRequest(
            Nodes: [new SaveCanvasNodeDto(nodeId, "TextNote", 0, 0, null, null, null, """{"text":"bye"}""", 0)],
            Edges: []));

        // Act
        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/roadmaps/{roadmapId}");

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.Roadmaps.AnyAsync(r => r.Id == roadmapId));
            Assert.False(await db.RoadmapNodes.AnyAsync(n => n.RoadmapId == roadmapId));
        });
    }

    // --- Helpers ---

    private async Task<Guid> CreateCourseInDb()
    {
        Guid courseId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            string slug = $"test-{Guid.NewGuid().ToString("N")[..8]}";
            var course = new Course(
                Guid.NewGuid(),
                Title.Create("Test Course").Value,
                Description.Create("Test course for roadmap tests").Value,
                CourseSlug.Create(slug).Value,
                SortKey.Initial());
            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync();
        });
        return courseId;
    }

    private async Task<Guid> CreateRoadmapInDb(
        string title,
        Guid? courseId = null,
        string? slug = null)
    {
        Guid roadmapId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var roadmap = new Roadmap(
                Guid.NewGuid(),
                Title.Create(title).Value,
                null,
                courseId,
                slug);

            db.Roadmaps.Add(roadmap);
            roadmapId = roadmap.Id;
            await db.SaveChangesAsync();
        });
        return roadmapId;
    }

    private async Task<Guid> CreateRoadmapViaApi(
        string title,
        Guid? courseId = null,
        string? slug = null)
    {
        var request = new CreateRoadmapRequest(title, null, courseId, slug);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/roadmaps", request);
        response.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(response);
    }

    private async Task SaveCanvasViaApi(Guid roadmapId, SaveCanvasRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/roadmaps/{roadmapId}/canvas")
            {
                Content = JsonContent.Create(request)
            });
        response.EnsureSuccessStatusCode();
    }
}
