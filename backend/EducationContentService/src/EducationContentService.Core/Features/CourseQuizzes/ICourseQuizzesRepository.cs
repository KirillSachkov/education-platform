using System.Linq.Expressions;
using EducationContentService.Domain.Courses;

namespace EducationContentService.Core.Features.CourseQuizzes;

/// <summary>
///     Репозиторий join-сущности <see cref="CourseQuiz"/> — зеркало
///     <c>ICourseMaterialsRepository</c> для квизов (#489). Attach-в-модуль
///     use-cases придут в ST-12; здесь — таблица + типовые методы.
/// </summary>
public interface ICourseQuizzesRepository
{
    Task AddAsync(CourseQuiz item, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Идемпотентная привязка квиза к курсу: создаёт строку с append-sort-key,
    ///     если пары (courseId, quizId) ещё нет. Возвращает <c>true</c>, если строка
    ///     была создана (паттерн <c>AddIfMissingAsync</c> из backend/CLAUDE.md).
    /// </summary>
    Task<bool> AddIfMissingAsync(Guid courseId, Guid quizId, CancellationToken cancellationToken = default);

    Task<Result<CourseQuiz, Error>> GetByAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        Expression<Func<CourseQuiz, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    Task<List<CourseQuiz>> GetManyByAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>Курсы, к которым привязан квиз (для access-тегов ENROLLED-квиза, ST-11).</summary>
    Task<List<Guid>> GetCourseIdsAsync(Guid quizId, CancellationToken cancellationToken = default);

    /// <summary>Каскад при hard-delete курса (bulk SQL).</summary>
    Task DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>Каскад при hard-delete квиза (bulk SQL).</summary>
    Task DeleteByQuizIdAsync(Guid quizId, CancellationToken cancellationToken = default);

    void Delete(CourseQuiz item);
}
