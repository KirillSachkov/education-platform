using TrainerService.Core.Database;
using TrainerService.Domain;

namespace TrainerService.Infrastructure.Postgres.Repositories;

/// <summary>
///     EF-проекция per-topic покрытия (#664), вычисляемая в БД (сущности не материализуются):
///     <list type="bullet">
///         <item>totals — COUNT вопросов по банкам каждой темы (не зависит от пользователя);</item>
///         <item>covered — DISTINCT (topic_id, question_id) с вердиктом CORRECT по сессиям юзера.</item>
///     </list>
///     DISTINCT берётся по паре, затем группируется в памяти — компактно (ограничено числом
///     верно отвеченных вопросов) и гарантированно транслируется в SQL.
/// </summary>
internal sealed class TopicCoverageReader : ITopicCoverageReader
{
    private readonly TrainerServiceDbContext _dbContext;

    public TopicCoverageReader(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyDictionary<Guid, TopicCoverage>> GetCoverageAsync(
        Guid userId,
        IReadOnlyCollection<Guid> topicIds,
        CancellationToken ct = default)
    {
        if (topicIds.Count == 0)
            return new Dictionary<Guid, TopicCoverage>();

        var ids = topicIds.ToHashSet();

        // Всего вопросов в банках каждой темы (user-independent).
        var totalRows = await _dbContext.TopicBanks
            .Where(b => ids.Contains(b.TopicId))
            .Join(_dbContext.TrainerQuestions, b => b.Id, q => q.BankId, (b, q) => b.TopicId)
            .GroupBy(topicId => topicId)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        Dictionary<Guid, int> totalsByTopic = totalRows.ToDictionary(r => r.TopicId, r => r.Count);

        // Distinct верно отвеченных вопросов по теме (CORRECT). Аноним (Guid.Empty) → ничего.
        Dictionary<Guid, int> coveredByTopic;
        if (userId == Guid.Empty)
        {
            coveredByTopic = [];
        }
        else
        {
            var correctPairs = await _dbContext.TrainingSessions
                .Where(s => s.UserId == userId)
                .SelectMany(s => s.Items)
                .Where(i => i.Verdict == AnswerVerdict.CORRECT && ids.Contains(i.TopicId))
                .Select(i => new { i.TopicId, i.QuestionId })
                .Distinct()
                .ToListAsync(ct);

            coveredByTopic = correctPairs
                .GroupBy(p => p.TopicId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        var result = new Dictionary<Guid, TopicCoverage>(ids.Count);
        foreach (Guid topicId in ids)
        {
            totalsByTopic.TryGetValue(topicId, out int total);
            coveredByTopic.TryGetValue(topicId, out int covered);
            result[topicId] = new TopicCoverage(covered, total);
        }

        return result;
    }
}
