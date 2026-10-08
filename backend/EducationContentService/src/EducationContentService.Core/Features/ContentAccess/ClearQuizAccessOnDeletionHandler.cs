using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Clears Redis access tags when a quiz is hard-deleted (#490).
/// </summary>
public static class ClearQuizAccessOnDeletionHandler
{
    public static async Task HandleAsync(
        QuizHardDeleted message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        await resourceAccessWriter.ClearTagsAsync(ResourceTypes.QUIZ, message.QuizId);

        logger.LogInformation(
            "Cleared access tags for deleted quiz. QuizId={QuizId}",
            message.QuizId);
    }
}
