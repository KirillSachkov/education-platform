namespace AssignmentReviewService.Domain.Reviews;

/// <summary>
///     Сообщение студента в его GitHub-PR, услышанное платформой через ARS webhook (#713).
///     Обратный канал к AI-ревью: студент отвечает reply'ем на inline-коммент бота или
///     пишет top-level коммент в PR — платформа кладёт это сюда и уведомляет автора курса.
///
///     Принадлежит <see cref="AiReview"/> через <see cref="AiReviewId"/> (logical FK на
///     ту проверку, чей PR прокомментировали). <see cref="GitHubCommentId"/> UNIQUE —
///     идемпотентность против повторной доставки того же webhook'а (GitHub ретраит).
///
///     Поля <see cref="AnsweredAt"/> / <see cref="AnswerBody"/> / <see cref="AnswerGitHubCommentId"/>
///     заполняет подзадача 1b, когда автор курса отвечает студенту через платформу.
/// </summary>
public sealed class StudentPrMessage : AggregateRoot
{
    private StudentPrMessage() { } // EF

    private StudentPrMessage(
        Guid aiReviewId,
        long gitHubCommentId,
        long? inReplyToGitHubId,
        StudentPrMessageKind kind,
        string authorGithubLogin,
        string body,
        string? path,
        int? line,
        string commentUrl,
        DateTimeOffset createdAtGithub,
        DateTimeOffset ingestedAt)
    {
        Id = Guid.Empty; // EF ValueGenerator (TimeOrderedGuidValueGenerator) — как у прочих ARS-агрегатов.
        AiReviewId = aiReviewId;
        GitHubCommentId = gitHubCommentId;
        InReplyToGitHubId = inReplyToGitHubId;
        Kind = kind;
        AuthorGithubLogin = authorGithubLogin;
        Body = body;
        Path = path;
        Line = line;
        CommentUrl = commentUrl;
        CreatedAtGithub = createdAtGithub;
        IngestedAt = ingestedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>FK на <see cref="AiReview"/> — проверку, чей PR прокомментировал студент.</summary>
    public Guid AiReviewId { get; private set; }

    /// <summary>GitHub comment id. UNIQUE — идемпотентность ingest'а.</summary>
    public long GitHubCommentId { get; private set; }

    /// <summary>
    ///     GitHub id inline-коммента, на который студент ответил reply'ем (<c>in_reply_to_id</c>).
    ///     null для top-level review-коммента и для <see cref="StudentPrMessageKind.ISSUE_COMMENT"/>.
    /// </summary>
    public long? InReplyToGitHubId { get; private set; }

    public StudentPrMessageKind Kind { get; private set; }

    public string AuthorGithubLogin { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Путь к файлу для inline review-коммента (иначе null).</summary>
    public string? Path { get; private set; }

    /// <summary>Строка diff'а для inline review-коммента (иначе null).</summary>
    public int? Line { get; private set; }

    /// <summary>Deep-link на тред PR (html_url коммента).</summary>
    public string CommentUrl { get; private set; } = string.Empty;

    /// <summary>Время создания коммента на GitHub (<c>comment.created_at</c>).</summary>
    public DateTimeOffset CreatedAtGithub { get; private set; }

    /// <summary>Когда платформа приняла коммент (ingest-время).</summary>
    public DateTimeOffset IngestedAt { get; private set; }

    /// <summary>Когда автор курса ответил студенту (заполнит 1b). null — без ответа.</summary>
    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>Текст ответа автора (заполнит 1b).</summary>
    public string? AnswerBody { get; private set; }

    /// <summary>GitHub comment id ответа автора, запощенного обратно в PR (заполнит 1b).</summary>
    public long? AnswerGitHubCommentId { get; private set; }

    public static StudentPrMessage Create(
        Guid aiReviewId,
        long gitHubCommentId,
        long? inReplyToGitHubId,
        StudentPrMessageKind kind,
        string authorGithubLogin,
        string body,
        string? path,
        int? line,
        string commentUrl,
        DateTimeOffset createdAtGithub,
        DateTimeOffset ingestedAt)
    {
        return new StudentPrMessage(
            aiReviewId,
            gitHubCommentId,
            inReplyToGitHubId,
            kind,
            authorGithubLogin,
            body,
            path,
            line,
            commentUrl,
            createdAtGithub,
            ingestedAt);
    }

    /// <summary>
    ///     Отметить сообщение отвеченным автором курса (подзадача 1b). <paramref name="answerGitHubCommentId"/>
    ///     — id ответного коммента, запощенного обратно в GitHub-тред (null, если ответ не ушёл в GitHub).
    /// </summary>
    public void MarkAnswered(string answerBody, long? answerGitHubCommentId, DateTimeOffset answeredAt)
    {
        AnswerBody = answerBody;
        AnswerGitHubCommentId = answerGitHubCommentId;
        AnsweredAt = answeredAt;
    }
}
