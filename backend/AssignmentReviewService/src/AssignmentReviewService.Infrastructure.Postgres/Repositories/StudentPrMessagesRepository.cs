using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;
using Core.Database;
using Dapper;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class StudentPrMessagesRepository : IStudentPrMessagesRepository
{
    private readonly AssignmentReviewServiceDbContext _db;
    private readonly ITransactionManager _transactions;

    public StudentPrMessagesRepository(
        AssignmentReviewServiceDbContext db,
        ITransactionManager transactions)
    {
        _db = db;
        _transactions = transactions;
    }

    public async Task AddAsync(StudentPrMessage message, CancellationToken ct = default) =>
        await _db.StudentPrMessages.AddAsync(message, ct);

    public Task<StudentPrMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default) =>
        _db.StudentPrMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public Task<bool> ExistsByGitHubCommentIdAsync(long gitHubCommentId, CancellationToken ct = default) =>
        _db.StudentPrMessages.AnyAsync(m => m.GitHubCommentId == gitHubCommentId, ct);

    public async Task<IReadOnlyList<StudentPrMessage>> GetByAiReviewIdAsync(
        Guid aiReviewId, CancellationToken ct = default) =>
        await _db.StudentPrMessages
            .Where(m => m.AiReviewId == aiReviewId)
            .OrderBy(m => m.CreatedAtGithub)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StudentPrMessage>> GetRecentByAiReviewIdAsync(
        Guid aiReviewId, int limit, CancellationToken ct = default)
    {
        // Берём последние @limit по created_at_github DESC (свежие релевантнее для промпта),
        // затем разворачиваем в хронологический (ascending) порядок для контекст-блока.
        List<StudentPrMessage> recent = await _db.StudentPrMessages
            .Where(m => m.AiReviewId == aiReviewId)
            .OrderByDescending(m => m.CreatedAtGithub)
            .Take(limit)
            .ToListAsync(ct);

        recent.Reverse();
        return recent;
    }

    public async Task MarkAnsweredAsync(
        Guid messageId,
        string answerBody,
        long? answerGitHubCommentId,
        DateTimeOffset answeredAt,
        CancellationToken ct = default)
    {
        // Targeted raw-SQL UPDATE (auto-commit) — self-contained write-helper для 1b, по образцу
        // AiReviewsRepository.HeartbeatRunningAsync. Не трогает поля ingest'а.
        const string sql = """
            UPDATE assignment_review.student_pr_messages
            SET answer_body = @AnswerBody,
                answer_github_comment_id = @AnswerGitHubCommentId,
                answered_at = @AnsweredAt
            WHERE id = @MessageId
            """;

        System.Data.Common.DbConnection conn = _transactions.GetDbConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                MessageId = messageId,
                AnswerBody = answerBody,
                AnswerGitHubCommentId = answerGitHubCommentId,
                AnsweredAt = answeredAt,
            },
            cancellationToken: ct));
    }
}
