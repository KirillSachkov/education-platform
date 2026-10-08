namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Lightweight projection used by CommentService inbox enrichment to decide
///     whether a comment should open under the course route or the standalone
///     knowledge-base route. Returned only for materials bound to a published
///     course; "primary" course = the earliest <c>course_materials</c> row,
///     selected via <c>DISTINCT ON (material_id) ORDER BY material_id, id</c>
///     (relies on <c>course_materials.id</c> being a Guid v7, time-ordered).
///     Materials with no published course binding simply do not appear in the
///     response.
/// </summary>
public sealed record MaterialCourseBindingLookupDto(
    Guid MaterialId,
    Guid CourseId,
    string CourseSlug);

public sealed record GetMaterialCourseBindingsRequest(IReadOnlyCollection<Guid> Ids);
