using ContentAccess;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Rebuilds Redis access tags when a quiz's access type changes (#490).
///     Симметрично <see cref="SyncMaterialAccessToRedisHandler"/>, но с PUBLISHED-гейтом:
///     теги существуют только у опубликованных квизов (у квиза нет created-события —
///     первый tag-set делает <see cref="SyncQuizAccessOnPublishHandler"/>), поэтому
///     смена доступа у DRAFT-квиза тегов не пишет.
/// </summary>
public static class SyncQuizAccessToRedisHandler
{
    public static async Task HandleAsync(
        QuizAccessChanged message,
        IQuizzesRepository quizzesRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        Result<Quiz, Error> quizResult = await quizzesRepository.GetByAsync(q => q.Id == message.QuizId);
        if (quizResult.IsFailure || quizResult.Value.Status != PublicationStatus.PUBLISHED)
        {
            logger.LogInformation(
                "Skipped quiz access tags sync — quiz is missing or not published. QuizId={QuizId}",
                message.QuizId);
            return;
        }

        List<Guid> courseIds = await courseQuizzesRepository.GetCourseIdsAsync(message.QuizId);

        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            message.AccessType,
            message.QuizId,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.QUIZ, message.QuizId, tags);

        logger.LogInformation(
            "Synced quiz access tags to Redis. QuizId={QuizId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.QuizId, message.AccessType, message.AuthorId, string.Join(", ", tags));
    }
}
