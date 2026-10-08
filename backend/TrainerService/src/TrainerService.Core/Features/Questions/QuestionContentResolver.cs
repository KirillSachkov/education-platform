using TrainerService.Core.Database;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Questions;

/// <summary>
///     Снапшот контента вопроса, нужный для cross-topic проекций (SRS-очередь, «Мои ошибки»):
///     стем + сложность + флаг free-сэмпла (для per-question замка #674). <c>QuestionStudyState</c>
///     хранит только id вопроса/темы, поэтому стем подтягивается из собственного банка тренажёра
///     один раз на набор тем (#623).
/// </summary>
public sealed record ResolvedQuestion(string Stem, string? Difficulty, bool IsFreeSample);

/// <summary>
///     Резолвит контент вопросов (стем/сложность) для набора тем тренажёра из СОБСТВЕННОГО банка
///     (#623, больше не из ECS): банки тем → их вопросы. Возвращает <c>questionId → ResolvedQuestion</c>.
///     Чисто read-only.
/// </summary>
public sealed class QuestionContentResolver
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;

    public QuestionContentResolver(ITopicBanksRepository banks, ITrainerQuestionsRepository questions)
    {
        _banks = banks;
        _questions = questions;
    }

    public async Task<Result<IReadOnlyDictionary<Guid, ResolvedQuestion>, Error>> ResolveAsync(
        IReadOnlyCollection<Guid> topicIds,
        CancellationToken ct)
    {
        var map = new Dictionary<Guid, ResolvedQuestion>();
        if (topicIds.Count == 0)
            return map;

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), ct);
        var bankIds = banks.Select(b => b.Id).ToList();
        if (bankIds.Count == 0)
            return map;

        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), ct);

        foreach (TrainerQuestion q in questions)
            map.TryAdd(q.Id, new ResolvedQuestion(q.Stem, q.Difficulty?.ToString(), q.IsFreeSample));

        return map;
    }
}
