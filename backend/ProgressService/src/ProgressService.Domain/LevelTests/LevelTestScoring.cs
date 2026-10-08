namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Детерминированная часть скоринга level-test попытки (ST-4, #479).
///     <list type="bullet">
///         <item>Очки вопроса по сложности: JUNIOR=1, MIDDLE=2, SENIOR=3, null/неизвестно → 1
///             (маппинг живёт ЗДЕСЬ — ECS-вопросы поля points не несут).</item>
///         <item>Choice: строгий set-match выбранного и правильного множеств (семантика
///             QuizAttempt #470) — полные очки или 0; неотвеченный = 0, в max входит.</item>
///         <item>OPEN_TEXT (и неизвестные типы): отвеченный — PendingAi=true и 0 заработанных
///             до AI-грейда; пустой — PendingAi=false (грейдить нечего).</item>
///         <item>Пока AI-грейдинг не READY — знаменатели секций считаются только по
///             choice-вопросам (open_text исключён); после READY open_text входит в max.</item>
///         <item>Percent секции и overall округляются AwayFromZero; overall — взвешенное
///             среднее процентов секций (секции с MaxPoints=0 исключаются).</item>
///     </list>
/// </summary>
public static class LevelTestScoring
{
    /// <summary>Секция для вопросов без явного section в answer-key.</summary>
    public const string GENERAL_SECTION_KEY = "general";

    /// <summary>Уровень-фолбэк, когда конфиг порогов пуст или ни один порог не достигнут.</summary>
    public const string DEFAULT_LEVEL = "JUNIOR";

    public const decimal DEFAULT_SECTION_WEIGHT = 1.0m;

    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MULTI_CHOICE = "MULTI_CHOICE";
    private const string EXACT_TEXT = "EXACT_TEXT";

    private const string DIFFICULTY_MIDDLE = "MIDDLE";
    private const string DIFFICULTY_SENIOR = "SENIOR";

    public static bool IsChoice(string questionType) =>
        string.Equals(questionType, SINGLE_CHOICE, StringComparison.Ordinal)
        || string.Equals(questionType, MULTI_CHOICE, StringComparison.Ordinal);

    /// <summary>EXACT_TEXT (#528) — рукописный точный ответ, грейдится детерминированно как choice.</summary>
    public static bool IsExactText(string questionType) =>
        string.Equals(questionType, EXACT_TEXT, StringComparison.Ordinal);

    /// <summary>Детерминированно грейдящиеся типы — входят в знаменатели всегда (без AI).</summary>
    public static bool IsDeterministic(string questionType) =>
        IsChoice(questionType) || IsExactText(questionType);

    /// <summary>
    ///     Нормализация для сверки точного ответа: регистронезависимо, учитываются только
    ///     буквы и цифры («1, 4, 9» == «149», «True False» == «true,false»). Делает ввод
    ///     устойчивым к пробелам/запятым/переносам, не прощая ошибок по существу.
    /// </summary>
    public static string NormalizeExactAnswer(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    public static decimal PointsForDifficulty(string? difficulty)
    {
        if (string.Equals(difficulty, DIFFICULTY_MIDDLE, StringComparison.Ordinal))
        {
            return 2m;
        }

        if (string.Equals(difficulty, DIFFICULTY_SENIOR, StringComparison.Ordinal))
        {
            return 3m;
        }

        // JUNIOR, null и неизвестные значения — базовый 1 балл.
        return 1m;
    }

    /// <summary>Грейдит ответы против вопросов answer-key (детерминированная часть, без AI).</summary>
    public static IReadOnlyList<LevelTestQuestionResult> GradeQuestions(
        IReadOnlyList<LevelTestQuestionKey> questions,
        IReadOnlyList<LevelTestAnswer> answers)
    {
        Dictionary<Guid, LevelTestAnswer> answersByQuestionId = answers.ToDictionary(a => a.QuestionId);

        var results = new List<LevelTestQuestionResult>(questions.Count);
        foreach (LevelTestQuestionKey question in questions)
        {
            LevelTestAnswer? answer = answersByQuestionId.GetValueOrDefault(question.Id);
            decimal maxPoints = PointsForDifficulty(question.Difficulty);

            // Снапшот вариантов/правильных/эталона/пояснения в результат (#561) —
            // разбор устойчив к правке и удалению квиза задним числом.
            IReadOnlyList<LevelTestOptionResult> optionSnapshot = (question.Options ?? [])
                .Select(o => new LevelTestOptionResult(o.Id, o.Text))
                .ToList();

            if (IsChoice(question.Type))
            {
                IReadOnlyList<Guid> selected = answer?.SelectedOptionIds ?? [];
                bool correct = selected.ToHashSet().SetEquals(question.CorrectOptionIds);

                results.Add(new LevelTestQuestionResult(
                    question.Id,
                    question.Section,
                    question.Difficulty,
                    question.Type,
                    maxPoints,
                    EarnedPoints: correct ? maxPoints : 0m,
                    IsCorrect: correct,
                    PendingAi: false,
                    AiScore: null,
                    AiFeedback: null,
                    Options: optionSnapshot,
                    CorrectOptionIds: question.CorrectOptionIds,
                    ReferenceAnswer: question.ReferenceAnswer,
                    Explanation: question.Explanation));

                continue;
            }

            if (IsExactText(question.Type))
            {
                // Рукописный точный ответ (#528): нормализованное сравнение с эталоном.
                // Неотвеченный/без эталона — 0; в max входит всегда, как choice.
                bool correct = answer?.TextAnswer is not null
                    && question.ReferenceAnswer is not null
                    && string.Equals(
                        NormalizeExactAnswer(answer.TextAnswer),
                        NormalizeExactAnswer(question.ReferenceAnswer),
                        StringComparison.Ordinal);

                results.Add(new LevelTestQuestionResult(
                    question.Id,
                    question.Section,
                    question.Difficulty,
                    question.Type,
                    maxPoints,
                    EarnedPoints: correct ? maxPoints : 0m,
                    IsCorrect: correct,
                    PendingAi: false,
                    AiScore: null,
                    AiFeedback: null,
                    Options: optionSnapshot,
                    CorrectOptionIds: question.CorrectOptionIds,
                    ReferenceAnswer: question.ReferenceAnswer,
                    Explanation: question.Explanation));

                continue;
            }

            // OPEN_TEXT и неизвестные будущие типы: грейдит AI (ST-5), если ответ дан.
            bool answered = answer?.TextAnswer is not null;
            results.Add(new LevelTestQuestionResult(
                question.Id,
                question.Section,
                question.Difficulty,
                question.Type,
                maxPoints,
                EarnedPoints: 0m,
                IsCorrect: null,
                PendingAi: answered,
                AiScore: null,
                AiFeedback: null,
                Options: optionSnapshot,
                CorrectOptionIds: question.CorrectOptionIds,
                ReferenceAnswer: question.ReferenceAnswer,
                Explanation: question.Explanation));
        }

        return results;
    }

    /// <summary>
    ///     Считает секции/overall/уровень/рекомендацию по per-вопрос результатам.
    ///     <paramref name="resolveSectionMeta"/> — источник Title/Weight/RecommendedCourseId
    ///     секции: при создании — определения из конфига, при пересчёте — снапшот из
    ///     уже сохранённых <see cref="LevelTestSectionScore"/>.
    /// </summary>
    public static LevelTestTotals ComputeTotals(
        IReadOnlyList<LevelTestQuestionResult> questionResults,
        Func<string, LevelTestSectionMeta> resolveSectionMeta,
        IReadOnlyList<LevelTestLevelThreshold> thresholds,
        Guid? fallbackCourseId,
        bool includeOpenText)
    {
        // Группируем по секции, сохраняя порядок первого появления (= порядок вопросов квиза).
        var sectionKeysInOrder = new List<string>();
        var grouped = new Dictionary<string, List<LevelTestQuestionResult>>(StringComparer.Ordinal);
        foreach (LevelTestQuestionResult result in questionResults)
        {
            string key = result.Section ?? GENERAL_SECTION_KEY;
            if (!grouped.TryGetValue(key, out List<LevelTestQuestionResult>? bucket))
            {
                bucket = [];
                grouped[key] = bucket;
                sectionKeysInOrder.Add(key);
            }

            bucket.Add(result);
        }

        var sections = new List<LevelTestSectionScore>(sectionKeysInOrder.Count);
        foreach (string key in sectionKeysInOrder)
        {
            decimal maxPoints = 0m;
            decimal earnedPoints = 0m;
            foreach (LevelTestQuestionResult question in grouped[key])
            {
                // EXACT_TEXT — детерминированный (#528): в знаменателях всегда, как choice.
                if (!IsDeterministic(question.Type) && !includeOpenText)
                {
                    continue;
                }

                maxPoints += question.MaxPoints;
                earnedPoints += question.EarnedPoints;
            }

            int percent = maxPoints == 0m
                ? 0
                : (int)Math.Round(100m * earnedPoints / maxPoints, MidpointRounding.AwayFromZero);

            LevelTestSectionMeta meta = resolveSectionMeta(key);
            sections.Add(new LevelTestSectionScore(
                key,
                meta.Title,
                percent,
                ResolveLevel(thresholds, percent),
                earnedPoints,
                maxPoints,
                meta.Weight,
                meta.RecommendedCourseId));
        }

        // Overall — взвешенное среднее процентов секций; MaxPoints=0 (нечего грейдить
        // в текущем режиме знаменателей) исключаются.
        decimal weightSum = 0m;
        decimal weightedPercentSum = 0m;
        foreach (LevelTestSectionScore section in sections)
        {
            if (section.MaxPoints == 0m)
            {
                continue;
            }

            weightSum += section.Weight;
            weightedPercentSum += section.Percent * section.Weight;
        }

        int overallPercent = weightSum == 0m
            ? 0
            : (int)Math.Round(weightedPercentSum / weightSum, MidpointRounding.AwayFromZero);

        LevelTestSectionScore? weakest = FindWeakestSection(sections);
        Guid? recommendedCourseId = weakest?.RecommendedCourseId ?? fallbackCourseId;

        return new LevelTestTotals(
            sections,
            overallPercent,
            ResolveLevel(thresholds, overallPercent),
            recommendedCourseId);
    }

    /// <summary>
    ///     Самая слабая секция — минимальный Percent среди секций с weight &gt; 0 и
    ///     MaxPoints &gt; 0 (секции без сигнала рекомендацию не выбирают). Тай-брейк —
    ///     первая по порядку вопросов.
    /// </summary>
    public static LevelTestSectionScore? FindWeakestSection(IReadOnlyList<LevelTestSectionScore> sections)
    {
        LevelTestSectionScore? weakest = null;
        foreach (LevelTestSectionScore section in sections)
        {
            if (section.Weight <= 0m || section.MaxPoints == 0m)
            {
                continue;
            }

            if (weakest is null || section.Percent < weakest.Percent)
            {
                weakest = section;
            }
        }

        return weakest;
    }

    /// <summary>Наибольший порог с MinPercent ≤ percent; пороги пусты / не достигнуты → нижний/дефолт.</summary>
    public static string ResolveLevel(IReadOnlyList<LevelTestLevelThreshold> thresholds, int percent)
    {
        if (thresholds.Count == 0)
        {
            return DEFAULT_LEVEL;
        }

        LevelTestLevelThreshold? best = null;
        LevelTestLevelThreshold? lowest = null;
        foreach (LevelTestLevelThreshold threshold in thresholds.OrderBy(t => t.MinPercent))
        {
            lowest ??= threshold;
            if (threshold.MinPercent <= percent)
            {
                best = threshold;
            }
        }

        return (best ?? lowest)!.Level;
    }
}

/// <summary>Title/Weight/RecommendedCourseId секции для расчёта тоталов.</summary>
public sealed record LevelTestSectionMeta(string Title, decimal Weight, Guid? RecommendedCourseId);

/// <summary>Итог расчёта тоталов попытки.</summary>
public sealed record LevelTestTotals(
    IReadOnlyList<LevelTestSectionScore> Sections,
    int OverallPercent,
    string Level,
    Guid? RecommendedCourseId);
