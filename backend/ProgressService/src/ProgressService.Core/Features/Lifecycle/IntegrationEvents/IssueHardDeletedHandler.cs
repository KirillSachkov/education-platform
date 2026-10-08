using ProgressService.Core.Abstractions;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.Core.Features.Lifecycle.IntegrationEvents;

public sealed class IssueHardDeletedHandler
{
    private readonly IIssueProgressRepository _issueProgressRepository;
    private readonly IModuleItemProgressRepository _moduleItemProgressRepository;
    private readonly IMaterialBookmarkRepository _bookmarkRepository;
    private readonly IIssueAuthorQuestionRepository _authorQuestionRepository;
    private readonly ILogger<IssueHardDeletedHandler> _logger;

    public IssueHardDeletedHandler(
        IIssueProgressRepository issueProgressRepository,
        IModuleItemProgressRepository moduleItemProgressRepository,
        IMaterialBookmarkRepository bookmarkRepository,
        IIssueAuthorQuestionRepository authorQuestionRepository,
        ILogger<IssueHardDeletedHandler> logger)
    {
        _issueProgressRepository = issueProgressRepository;
        _moduleItemProgressRepository = moduleItemProgressRepository;
        _bookmarkRepository = bookmarkRepository;
        _authorQuestionRepository = authorQuestionRepository;
        _logger = logger;
    }

    public async Task Handle(IssueHardDeleted message, CancellationToken cancellationToken)
    {
        // IssueSubmission cascades via FK (ON DELETE CASCADE) from IssueProgress
        int issueProgress = await _issueProgressRepository.DeleteByIssueIdAsync(
            message.IssueId, cancellationToken);

        int moduleItems = await _moduleItemProgressRepository.DeleteByReferenceIdAsync(
            message.IssueId, cancellationToken);

        int bookmarks = await _bookmarkRepository.DeleteByTargetEntityIdsAsync(
            [message.IssueId], cancellationToken);

        // #693 — приватные вопросы автору живут на issueId (без FK), чистим явно.
        int authorQuestions = await _authorQuestionRepository.DeleteByIssueIdAsync(
            message.IssueId, cancellationToken);

        _logger.LogInformation(
            "IssueHardDeleted {IssueId}: deleted {IssueProgress} issue progress (+ cascaded submissions), {ModuleItems} module items, {Bookmarks} bookmarks, {AuthorQuestions} author questions",
            message.IssueId,
            issueProgress,
            moduleItems,
            bookmarks,
            authorQuestions);
    }
}
