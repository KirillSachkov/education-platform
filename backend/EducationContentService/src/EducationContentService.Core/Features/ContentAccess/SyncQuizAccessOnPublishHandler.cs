using ContentAccess;
using EducationContentService.Core.Features.CourseQuizzes;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Sets Redis access tags when a quiz is published (#490).
///     Зеркало материала с одним отличием: у квиза нет <c>quiz.created</c>-события —
///     теги существуют только у PUBLISHED-квизов, и точка их появления — publish.
///     CourseIds резолвятся свежими из <c>course_quizzes</c> (а не из payload) —
///     устойчивость к out-of-order доставке.
/// </summary>
public static class SyncQuizAccessOnPublishHandler
{
    public static async Task HandleAsync(
        QuizPublished message,
        ICourseQuizzesRepository courseQuizzesRepository,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        List<Guid> courseIds = await courseQuizzesRepository.GetCourseIdsAsync(message.QuizId);

        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            message.AccessType,
            message.QuizId,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.QUIZ, message.QuizId, tags);

        logger.LogInformation(
            "Set access tags for published quiz. QuizId={QuizId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.QuizId, message.AccessType, message.AuthorId, string.Join(", ", tags));
    }
}
