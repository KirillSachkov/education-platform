namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Body для <c>POST /internal/access/users/{userId}/covered-courses</c>. Опциональный
/// <paramref name="AuthorId"/> фильтрует результат до курсов конкретного автора
/// (зеркало <c>GetMyCourseProgress?authorId=</c>). <c>null</c> — все покрытые курсы.
/// </summary>
public sealed record CoveredCoursesRequest(Guid? AuthorId);
