using System.Net;
using System.Text.Json;
using ProgressService.Contracts.Dtos;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class GetMyEnrollmentTests : ProgressServiceTestsBase
{
    public GetMyEnrollmentTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyEnrollment_WhenEnrolled_ReturnsEnrollmentWithProgress()
    {
        // Arrange
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 1);
        EducationContentClient.AddMaterial(moduleId, lessonId, 1);
        EducationContentClient.AddMaterialCourseContext(lessonId, courseId, moduleId, moduleItemsTotal: 1);

        // Seed a progress anchor (per-course enroll endpoints removed in access-derive-model Phase 4).
        await SeedEnrollmentAsync(courseId, userId);

        // Start module and view lesson to create progress records
        HttpResponseMessage startModuleResponse = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");
        Assert.Equal(HttpStatusCode.OK, startModuleResponse.StatusCode);

        HttpResponseMessage viewLessonResponse = await PostAsync(
            $"/progress/materials/{lessonId}/view");
        Assert.Equal(HttpStatusCode.OK, viewLessonResponse.StatusCode);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/my-enrollment");

        // Assert
        response.EnsureSuccessStatusCode();

        CourseEnrollmentProgressDto? dto = await ReadWrappedResultAsync<CourseEnrollmentProgressDto>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(1, dto.MaterialsTotal);
        Assert.Equal(1, dto.MaterialsViewed);
        Assert.Equal(1, dto.ModulesTotal);
        Assert.Equal(1, dto.ModulesCompleted);
    }

    [Fact]
    public async Task GetMyEnrollment_WhenNotEnrolledAndNotEntitled_ReturnsNull()
    {
        // Arrange — derive-модель (Phase 1): null теперь только когда юзер НЕ entitled.
        // Entitled-but-no-row отдаёт synthetic zeroed DTO (см. DerivedCourseReadsTests).
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/my-enrollment");

        // Assert
        response.EnsureSuccessStatusCode();

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        // The result should be null for a non-enrolled user
        Assert.True(
            root.TryGetProperty("result", out JsonElement resultElement)
            && resultElement.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMyEnrollment_EnrolledWithNoProgress_ReturnsZeroCounts()
    {
        // Arrange
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);

        // Seed a progress anchor but don't do any progress
        await SeedEnrollmentAsync(courseId, userId);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/my-enrollment");

        // Assert
        response.EnsureSuccessStatusCode();

        CourseEnrollmentProgressDto? dto = await ReadWrappedResultAsync<CourseEnrollmentProgressDto>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(0, dto.MaterialsTotal);
        Assert.Equal(0, dto.MaterialsViewed);
        Assert.Equal(0, dto.ModulesTotal);
        Assert.Equal(0, dto.ModulesCompleted);
        Assert.Equal(0, dto.IssuesTotal);
        Assert.Equal(0, dto.IssuesCompleted);
    }

    [Fact]
    public async Task GetMyEnrollment_ReturnsOnlyCurrentUserEnrollment()
    {
        // Arrange
        Guid courseId = Guid.NewGuid();
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, hasFreeContent: true);

        // Seed anchors for both users
        await SeedEnrollmentAsync(courseId, user1Id);
        await SeedEnrollmentAsync(courseId, user2Id);

        // Act — query as user 1
        AuthenticateAs(user1Id, "platform-participant");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/my-enrollment");

        // Assert
        response.EnsureSuccessStatusCode();

        CourseEnrollmentProgressDto? dto = await ReadWrappedResultAsync<CourseEnrollmentProgressDto>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
    }
}
