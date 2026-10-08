namespace EducationContentService.Contracts.Issues;

/// <summary>
///     Lightweight projection used by CommentService inbox enrichment to route
///     a comment on an issue to the course-scoped issue view.
///     Resolves issue → project → primary published course (the earliest
///     <c>course_items</c> row for the project, <c>item_type = 'Project'</c>),
///     selected via <c>DISTINCT ON (issue.id) ORDER BY issue.id, ci.id</c>.
///     Issues with no published course binding simply do not appear in the
///     response.
/// </summary>
public sealed record IssueCourseBindingLookupDto(
    Guid IssueId,
    Guid CourseId,
    string CourseSlug);

public sealed record GetIssueCourseBindingsRequest(IReadOnlyCollection<Guid> Ids);
