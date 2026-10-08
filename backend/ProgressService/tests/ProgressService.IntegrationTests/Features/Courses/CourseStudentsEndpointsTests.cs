using System.Net;
using System.Text.Json;
using ProgressService.Contracts.Dtos;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseStudentsEndpointsTests : ProgressServiceTestsBase
{
    public CourseStudentsEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // Derive-модель (epic access-derive-model, Phase 1): ростер выводится из grant-holders
    // (AccessService), а НЕ из course_enrollments. Поэтому тесты сидят grantees через mock,
    // и доказывают, что ростер не пуст даже при ОТСУТСТВИИ enrollment-строк (фикс бага
    // «пустая вкладка Студенты» на новом курсе).

    [Fact]
    public async Task AuthorOfCourse_SeesGrantHolders_WithZeroEnrollmentRows()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(studentId, "student1@test.com", "Student One");
        AccessServiceClient.AddCourseGrantee(courseId, studentId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);

        // КЛЮЧЕВОЙ assert: нет ни одной course_enrollments строки, но grant-holder в ростере.
        int enrollmentCount = await ExecuteInDb(db =>
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                db.CourseEnrollments));
        Assert.Equal(0, enrollmentCount);

        Assert.Single(result.Items);
        Assert.Equal(studentId, result.Items[0].UserId);
        Assert.Equal("student1@test.com", result.Items[0].Email);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task AuthorOfAnotherCourse_CannotSeeStudents()
    {
        Guid realAuthorId = Guid.NewGuid();
        Guid strangerAuthorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: realAuthorId, hasFreeContent: true);
        AuthenticateAs(strangerAuthorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_CanSeeAnyCourseStudents()
    {
        Guid authorId = Guid.NewGuid();
        Guid adminId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(studentId, "student3@test.com", "Student Three");
        AccessServiceClient.AddCourseGrantee(courseId, studentId);
        AuthenticateAs(adminId, "platform-admin");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetCourseStudents_MergesLocalProgress_WhenEnrollmentRowExists()
    {
        // Grant-holder, который уже начал — enrollment-строка существует. Ростер должен отдать
        // её enrollmentId/enrolledAt вместо синтетических grant-значений.
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(studentId, "merged@test.com", "Merged Student");
        AccessServiceClient.AddCourseGrantee(courseId, studentId);

        Guid enrollmentId = await SeedEnrollmentAsync(courseId, studentId, authorId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);

        CourseStudentDto item = Assert.Single(result.Items);
        Assert.Equal(studentId, item.UserId);
        Assert.Equal(enrollmentId, item.EnrollmentId);
    }

    [Fact]
    public async Task GetCourseStudents_SearchByName_ReturnsMatchedGrantHolder()
    {
        Guid authorId = Guid.NewGuid();
        Guid matchId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(matchId, "search-me@test.com", "search-me");
        AuthServiceClient.AddUser(otherId, "other@test.com", "Someone Else");
        AccessServiceClient.AddCourseGrantee(courseId, matchId);
        AccessServiceClient.AddCourseGrantee(courseId, otherId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20&search=search");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);

        Assert.Single(result.Items);
        Assert.Equal("search-me", result.Items[0].Name);
    }

    [Fact]
    public async Task GetCourseStudents_SearchByEmail_ReturnsEmpty()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(
            studentId,
            "course-email-needle@test.com",
            name: "Email Only Student",
            username: "course_email_user");
        AccessServiceClient.AddCourseGrantee(courseId, studentId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20&search=course-email-needle");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetCourseStudents_SearchByTelegram_ReturnsMatchedGrantHolder()
    {
        Guid authorId = Guid.NewGuid();
        Guid matchId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(
            matchId,
            "telegram-match@test.com",
            name: "Telegram Student",
            username: "telegram_student",
            telegramUsername: "course_tg_unique");
        AuthServiceClient.AddUser(otherId, "other-telegram@test.com", "Someone Else");
        AccessServiceClient.AddCourseGrantee(courseId, matchId);
        AccessServiceClient.AddCourseGrantee(courseId, otherId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20&search=course_tg_unique");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);

        CourseStudentDto item = Assert.Single(result.Items);
        Assert.Equal(matchId, item.UserId);
    }

    [Fact]
    public async Task GetCourseStudents_SearchByName_NoAuthMatch_ReturnsEmpty()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AuthServiceClient.AddUser(studentId, "student@test.com", "Student");
        AccessServiceClient.AddCourseGrantee(courseId, studentId);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage listResponse = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20&search=nonexistent");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        PaginationResponse<CourseStudentDto> result =
            await ReadWrappedResultAsync<PaginationResponse<CourseStudentDto>>(listResponse);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetCourseStudents_WithoutAuth_ShouldReturn401()
    {
        Guid courseId = Guid.NewGuid();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseStudents_WithoutCoursesManagePermission_ShouldReturn403()
    {
        Guid courseId = Guid.NewGuid();
        Guid participantId = Guid.NewGuid();

        AuthenticateAs(participantId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseStudents_ForbiddenForStranger_ReturnsForbiddenErrorCode()
    {
        Guid courseAuthorId = Guid.NewGuid();
        Guid otherAuthorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: courseAuthorId, hasFreeContent: true);
        AuthenticateAs(otherAuthorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement error = document.RootElement.GetProperty("error");
        JsonElement messages = error.GetProperty("messages");
        string errorCode = messages[0].GetProperty("code").GetString()!;

        Assert.Equal("course.management.forbidden", errorCode);
    }

    private async Task<Guid> SeedEnrollmentAsync(Guid courseId, Guid userId, Guid authorId)
    {
        Guid enrollmentId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            ProgressService.Domain.Enrollments.CourseEnrollment enrollment =
                ProgressService.Domain.Enrollments.CourseEnrollment.CreateAnchor(
                    userId,
                    courseId,
                    authorId,
                    ProgressService.Domain.Enrollments.EnrollmentSource.ENGAGEMENT).Value;
            enrollmentId = enrollment.Id;
            db.CourseEnrollments.Add(enrollment);
            await db.SaveChangesAsync();
        });
        return enrollmentId;
    }
}
