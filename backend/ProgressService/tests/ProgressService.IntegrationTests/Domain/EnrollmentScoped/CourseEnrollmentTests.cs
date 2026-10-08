using ProgressService.Domain.Enrollments;

// access-derive-model (#367): CourseEnrollment is a pure lazy progress-anchor.
// Archive/Unarchive/Create/SortKey machinery removed — only CreateAnchor remains.
namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class CourseEnrollmentTests
{
    [Fact]
    public void CreateAnchor_WithValidIds_CreatesEnrollment()
    {
        var result = CourseEnrollment.CreateAnchor(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            EnrollmentSource.ENGAGEMENT);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.Equal(EnrollmentSource.ENGAGEMENT, result.Value.Source);
    }

    [Fact]
    public void CreateAnchor_WithEmptyUserId_Fails()
    {
        var result = CourseEnrollment.CreateAnchor(
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            EnrollmentSource.ENGAGEMENT);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void CreateAnchor_WithEmptyCourseId_Fails()
    {
        var result = CourseEnrollment.CreateAnchor(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            EnrollmentSource.ENGAGEMENT);

        Assert.True(result.IsFailure);
    }
}
