namespace TrainerService.Domain.Questions;

/// <summary>
///     Доменный сервис (#674): пересчитывает <see cref="TrainerQuestion.IsFreeSample"/> по всем
///     вопросам ОДНОЙ темы — детерминированный «free-сэмпл» (≈<c>percent</c>% автогрейдимых вопросов
///     на каждый bucket сложности доступны не-PRO как бесплатная проба).
///     <para>
///         Правила:
///         <list type="bullet">
///             <item>Eligible = только автогрейдимые типы (SINGLE_CHOICE, MULTI_CHOICE, EXACT_TEXT).
///                 <b>OPEN_TEXT никогда не free</b> (AI-грейд платный) — всегда <c>IsFreeSample=false</c>,
///                 исключён из bucket'ов.</item>
///             <item>Группировка eligible по <see cref="TrainerQuestion.Difficulty"/> bucket
///                 (<c>null</c> — собственный bucket).</item>
///             <item>Внутри bucket free = первые <c>max(1, ceil(percent/100 * bucketCount))</c>
///                 вопросов по возрастанию <see cref="TrainerQuestion.SortKey"/> (ordinal-сравнение,
///                 как порядок вопросов везде в коде). Так автор курирует, какие именно бесплатны,
///                 через порядок. Минимум 1 на bucket — у темы всегда есть бесплатная проба.</item>
///         </list>
///     </para>
/// </summary>
public static class TrainerFreeAllocationPolicy
{
    public static bool IsAutoGradable(TrainerQuestionType type) =>
        type is TrainerQuestionType.SINGLE_CHOICE
            or TrainerQuestionType.MULTI_CHOICE
            or TrainerQuestionType.EXACT_TEXT;

    /// <summary>
    ///     Пересчитывает <see cref="TrainerQuestion.IsFreeSample"/> по всему набору вопросов темы.
    ///     Мутирует переданные агрегаты (caller сохраняет в той же транзакции).
    /// </summary>
    /// <param name="topicQuestions">Все вопросы темы (по всем её банкам).</param>
    /// <param name="percent">Доля бесплатных вопросов на bucket, %. Из <c>TrainerOptions.FreeSamplePercent</c>.</param>
    public static void Recompute(IEnumerable<TrainerQuestion> topicQuestions, int percent)
    {
        List<TrainerQuestion> all = topicQuestions.ToList();

        // Старт: всё заперто (в т.ч. OPEN_TEXT — он никогда не разблокируется ниже).
        foreach (TrainerQuestion question in all)
            question.SetFreeSample(false);

        IEnumerable<IGrouping<QuestionDifficulty?, TrainerQuestion>> buckets = all
            .Where(q => IsAutoGradable(q.Type))
            .GroupBy(q => q.Difficulty);

        foreach (IGrouping<QuestionDifficulty?, TrainerQuestion> bucket in buckets)
        {
            List<TrainerQuestion> ordered = bucket
                .OrderBy(q => q.SortKey, StringComparer.Ordinal)
                .ToList();

            int freeCount = Math.Min(
                ordered.Count,
                Math.Max(1, (int)Math.Ceiling(percent / 100.0 * ordered.Count)));

            for (int i = 0; i < freeCount; i++)
                ordered[i].SetFreeSample(true);
        }
    }
}
