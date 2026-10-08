namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Phase 8 (#15) добавил 4 AI-денорм поля как часть positional ctor.
///     Они селектятся в SQL (см. <c>GetPendingReviewIssues</c>,
///     <c>GetInReviewIssues</c> и <c>GetReviewedReviewIssues</c>), поэтому
///     Dapper'у нужен ctor с этими параметрами. Init-only properties
///     (StudentName, etc.) НЕ селектятся в основном SQL — они enrich'атся
///     <c>ReviewUserEnricher</c>'ом отдельно.
///
///     <c>AttemptsCount</c> (#369) — сколько всего попыток в группе
///     «студент + задание» (issue_progress). Обзорные query схлопывают группу
///     до последней попытки (представителя) и считают attempts окном
///     <c>COUNT(*) OVER (PARTITION BY issue_progress_id)</c>. Для одиночной
///     отправки = 1.
///
///     <c>AuthorHelpRequestedAt</c> (#383) — timestamp когда студент нажал
///     «Позвать автора»; null = автора не звали. Селектится в pending / in-review
///     query, чтобы автор видел бейдж «Нужна помощь автора» на карточке.
///
///     <c>StudentQuestionAt</c> (#713) — timestamp последнего вопроса, который студент
///     задал в своём GitHub-PR; null = вопросов не было. Денормится из
///     <c>StudentPrQuestionAsked</c> event'а (ARS) и селектится во всех трёх review-list
///     query, питая бейдж «новый вопрос от студента» на карточке сдачи.
/// </summary>
public sealed record ReviewIssueItemDto(
    Guid SubmissionId,
    Guid CourseId,
    Guid ProjectId,
    Guid IssueId,
    Guid StudentId,
    int SubmissionNo,
    string Payload,
    string IssueProgressStatus,
    string ReviewStatus,
    Guid? ReviewerId,
    DateTime SubmittedAt,
    DateTime? ReviewStartedAt,
    DateTime? ReviewedAt,
    string? Feedback,
    string? LatestAiVerdict,
    int AiIterationsCount,
    DateTime? LastAiIterationAt,
    string? AiReviewStatus,
    DateTime? AuthorHelpRequestedAt,
    string? AuthorHelpMessage,
    DateTime? StudentQuestionAt,
    int AttemptsCount)
{
    public string? StudentName { get; init; }
    public string? StudentUsername { get; init; }

    // Контакты студента для связи (enrich'атся ReviewUserEnricher'ом из AuthService) — #575.
    public string? StudentEmail { get; init; }
    public string? StudentTelegramUsername { get; init; }
    public Guid? StudentAvatarId { get; init; }
    public string? ReviewerName { get; init; }
    public string? ReviewerUsername { get; init; }
    public Guid? ReviewerAvatarId { get; init; }
    public string? CourseTitle { get; init; }
    public string? ProjectTitle { get; init; }
    public string? IssueTitle { get; init; }
}
