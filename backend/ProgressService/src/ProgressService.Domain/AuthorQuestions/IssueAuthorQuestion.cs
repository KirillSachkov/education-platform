namespace ProgressService.Domain.AuthorQuestions;

/// <summary>
///     Приватный вопрос студента автору по заданию, заданный ДО отправки решения (#693).
///     User-scoped: одна строка на пару (UserId, IssueId) — одноразовый сигнал «позвал автора»
///     (как submission-scoped «Позвать автора» #383, но без сабмишена). Повторный вопрос
///     по тому же заданию идемпотентен: запись уже есть → событие повторно не публикуется.
///     Виден только владельцу и автору (через уведомление) — Tier-3 entitlement по самому
///     заданию проверяется на write-эндпоинте.
/// </summary>
public sealed class IssueAuthorQuestion
{
    public const int MESSAGE_MAX_LENGTH = 2000;

    private IssueAuthorQuestion(Guid id, Guid userId, Guid issueId, string message, DateTime askedAt)
    {
        Id = id;
        UserId = userId;
        IssueId = issueId;
        Message = message;
        AskedAt = askedAt;
    }

    private IssueAuthorQuestion()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid IssueId { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTime AskedAt { get; private set; }

    public static Result<IssueAuthorQuestion, Error> Create(
        Guid userId,
        Guid issueId,
        string? message,
        DateTime askedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (issueId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(issueId));
        }

        string trimmed = message?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ProgressErrors.IssueAuthorQuestionMessageRequired();
        }

        if (trimmed.Length > MESSAGE_MAX_LENGTH)
        {
            return ProgressErrors.IssueAuthorQuestionMessageTooLong(MESSAGE_MAX_LENGTH);
        }

        return new IssueAuthorQuestion(Guid.CreateVersion7(), userId, issueId, trimmed, askedAtUtc);
    }
}
