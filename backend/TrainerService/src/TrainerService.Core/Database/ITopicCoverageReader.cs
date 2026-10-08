namespace TrainerService.Core.Database;

/// <summary>
///     Per-topic «освоение» = ПОКРЫТИЕ темы (#664): сколько различных вопросов темы пользователь
///     решил ВЕРНО (<c>CoveredCount</c>) из общего числа вопросов во всех банках темы
///     (<c>TotalCount</c>). В отличие от EWMA-mastery (он взлетает до 100% с 1-2 верных ответов),
///     покрытие честно отражает, какую долю темы человек реально прошёл.
/// </summary>
public readonly record struct TopicCoverage(int CoveredCount, int TotalCount)
{
    /// <summary>round(100*covered/total), зажато в [0..100]; 0 если в теме нет вопросов.</summary>
    public int Percent =>
        TotalCount <= 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(100.0 * CoveredCount / TotalCount, MidpointRounding.AwayFromZero),
                0,
                100);
}

/// <summary>
///     Считает per-topic покрытие («освоение») вызывающего: распределённый Dapper/EF-read,
///     не материализующий сущности. Покрытие = distinct верно отвеченных вопросов темы / всего
///     вопросов в банках темы. Питает <c>GET /trainer/topics</c> и <c>GET /trainer/progress</c>.
/// </summary>
public interface ITopicCoverageReader
{
    /// <summary>
    ///     Покрытие по каждой теме из <paramref name="topicIds"/> для пользователя
    ///     <paramref name="userId"/> (<see cref="Guid.Empty"/> = аноним → covered 0 везде).
    ///     Темы без записи в результате трактуются вызывающим как нулевое покрытие.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TopicCoverage>> GetCoverageAsync(
        Guid userId,
        IReadOnlyCollection<Guid> topicIds,
        CancellationToken ct = default);
}
