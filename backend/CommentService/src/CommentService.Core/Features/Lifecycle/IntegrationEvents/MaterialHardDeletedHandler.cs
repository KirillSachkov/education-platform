using Common;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace CommentService.Core.Features.Lifecycle.IntegrationEvents;

/// <summary>
///     Каскадно удаляет ВСЕ комментарии (включая soft-deleted) для материала, который был
///     удалён в EducationContentService. После hard-delete материала тред становится
///     unreachable orphan-данными — пользователь не может попасть на страницу материала.
/// </summary>
public sealed class MaterialHardDeletedHandler
{
    private readonly ICommentsRepository _commentsRepository;
    private readonly ILogger<MaterialHardDeletedHandler> _logger;

    public MaterialHardDeletedHandler(
        ICommentsRepository commentsRepository,
        ILogger<MaterialHardDeletedHandler> logger)
    {
        _commentsRepository = commentsRepository;
        _logger = logger;
    }

    public async Task Handle(MaterialHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _commentsRepository.DeleteByTargetEntityAsync(
            EntityType.Material, message.MaterialId, cancellationToken);

        _logger.LogInformation(
            "MaterialHardDeleted {MaterialId}: deleted {Count} comment(s)",
            message.MaterialId,
            deleted);
    }
}

public sealed class CourseHardDeletedHandler
{
    private CourseHardDeletedHandler()
    {
    }

    public static async Task Handle(
        CourseHardDeleted message,
        ICommentsRepository commentsRepository,
        ILogger<CourseHardDeletedHandler> logger,
        CancellationToken cancellationToken)
    {
        int deleted = await commentsRepository.DeleteByTargetEntityAsync(
            EntityType.Course, message.CourseId, cancellationToken);

        logger.LogInformation(
            "CourseHardDeleted {CourseId}: deleted {Count} comment(s)",
            message.CourseId,
            deleted);
    }
}

public sealed class IssueHardDeletedHandler
{
    private IssueHardDeletedHandler()
    {
    }

    public static async Task Handle(
        IssueHardDeleted message,
        ICommentsRepository commentsRepository,
        ILogger<IssueHardDeletedHandler> logger,
        CancellationToken cancellationToken)
    {
        int deleted = await commentsRepository.DeleteByTargetEntityAsync(
            EntityType.Issue, message.IssueId, cancellationToken);

        logger.LogInformation(
            "IssueHardDeleted {IssueId}: deleted {Count} comment(s)",
            message.IssueId,
            deleted);
    }
}

public sealed class QuizHardDeletedHandler
{
    private QuizHardDeletedHandler()
    {
    }

    public static async Task Handle(
        QuizHardDeleted message,
        ICommentsRepository commentsRepository,
        ILogger<QuizHardDeletedHandler> logger,
        CancellationToken cancellationToken)
    {
        int deleted = await commentsRepository.DeleteByTargetEntityAsync(
            EntityType.Quiz, message.QuizId, cancellationToken);

        logger.LogInformation(
            "QuizHardDeleted {QuizId}: deleted {Count} comment(s)",
            message.QuizId,
            deleted);
    }
}
