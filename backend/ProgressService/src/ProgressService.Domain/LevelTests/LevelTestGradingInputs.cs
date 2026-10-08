namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Доменная проекция вопроса answer-key для грейдинга level-test попытки —
///     ProgressService.Domain не ссылается на ECS Contracts, поэтому use-case
///     мапит <c>QuizAnswerKeyQuestionDto</c> в этот тип. Issue #479.
///     <see cref="Options"/> + <see cref="Explanation"/> (#561) снапшотятся в
///     <see cref="LevelTestQuestionResult"/> для разбора с правильными ответами.
/// </summary>
public sealed record LevelTestQuestionKey(
    Guid Id,
    string Type,
    string? Section,
    string? Difficulty,
    IReadOnlyList<Guid> CorrectOptionIds,
    string? ReferenceAnswer = null,
    IReadOnlyList<LevelTestOptionKey>? Options = null,
    string? Explanation = null);

/// <summary>Вариант ответа (id+text) для снапшота разбора level-test попытки (#561).</summary>
public sealed record LevelTestOptionKey(Guid Id, string Text);

/// <summary>Доменная проекция секции level-test конфига (ECS <c>LevelTestSectionDto</c>).</summary>
public sealed record LevelTestSectionDefinition(
    string Key,
    string Title,
    decimal Weight,
    Guid? RecommendedCourseId);

/// <summary>Порог уровня: уровень присваивается по наибольшему порогу с MinPercent ≤ percent.</summary>
public sealed record LevelTestLevelThreshold(string Level, int MinPercent);

/// <summary>
///     Снапшот квизного грейдинг-конфига на момент сабмита — JSONB-колонка
///     <c>level_test_attempts.grading_config</c>. Нужен для пересчёта уровней после
///     AI-грейдинга (ST-5 только подаёт баллы — пороги/фолбэк уже в агрегате);
///     веса и recommendedCourseId секций живут в <see cref="LevelTestSectionScore"/>.
/// </summary>
public sealed record LevelTestGradingConfig(
    IReadOnlyList<LevelTestLevelThreshold> Thresholds,
    Guid? FallbackCourseId);
