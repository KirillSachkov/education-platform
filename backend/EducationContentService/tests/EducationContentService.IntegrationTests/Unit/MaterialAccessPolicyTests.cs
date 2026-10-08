using CSharpFunctionalExtensions;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Unit;

/// <summary>
///     Unit-тесты для <see cref="MaterialAccessPolicy" /> — чистая доменная логика без инфраструктуры.
///     После plan-bound рефакторинга (#77) INV-3 снята: orphan FREE/ENROLLED легитимны,
///     <see cref="MaterialAccessPolicy.CanSetAccessType"/> всегда успешен.
///     Гейтинг orphan-материалов ушёл на уровень Redis-тегов через
///     <see cref="EducationContentService.Core.Features.ContentAccess.ContentAccessTagBuilder"/>
///     (см. <see cref="ContentAccessTagBuilderTests"/>).
/// </summary>
public sealed class MaterialAccessPolicyTests
{
    [Theory]
    [InlineData(AccessType.PUBLIC, 0)]
    [InlineData(AccessType.PUBLIC, 1)]
    [InlineData(AccessType.PUBLIC, 5)]
    [InlineData(AccessType.REGISTERED, 0)]
    [InlineData(AccessType.REGISTERED, 5)]
    // Issue #358: AccessType.FREE удалён; legacy строки collapsed → REGISTERED data-миграцией.
    [InlineData(AccessType.ENROLLED, 0)] // orphan ENROLLED — теперь разрешён
    [InlineData(AccessType.ENROLLED, 1)]
    [InlineData(AccessType.ENROLLED, 3)]
    public void CanSetAccessType_AllCombinations_Allowed_AfterPlanBoundRefactor(
        AccessType accessType, int boundCourseCount)
    {
        UnitResult<Error> result = MaterialAccessPolicy.CanSetAccessType(accessType, boundCourseCount);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void CanSetAccessType_NegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MaterialAccessPolicy.CanSetAccessType(AccessType.PUBLIC, boundCourseCount: -1));
    }

    [Fact]
    public void Material_Update_Accepts_Orphan_Enrolled_AfterPlanBoundRefactor()
    {
        var material = new Material(
            Guid.NewGuid(),
            Title.Create("Test").Value,
            MaterialKind.ARTICLE,
            AccessType.PUBLIC);

        UnitResult<Error> result = material.Update(
            Title.Create("Test").Value,
            content: null,
            MaterialKind.ARTICLE,
            AccessType.ENROLLED,
            boundCourseCount: 0,
            description: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.ENROLLED, material.AccessType);
    }

    [Fact]
    public void Material_Update_Accepts_Enrolled_With_Courses()
    {
        var material = new Material(
            Guid.NewGuid(),
            Title.Create("Test").Value,
            MaterialKind.ARTICLE,
            AccessType.PUBLIC);

        UnitResult<Error> result = material.Update(
            Title.Create("Test").Value,
            content: null,
            MaterialKind.ARTICLE,
            AccessType.ENROLLED,
            boundCourseCount: 1,
            description: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccessType.ENROLLED, material.AccessType);
    }
}
