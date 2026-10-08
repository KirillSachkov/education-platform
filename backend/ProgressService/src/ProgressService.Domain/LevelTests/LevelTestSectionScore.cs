namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Итог секции level-test попытки — элемент JSONB-массива
///     <c>level_test_attempts.section_scores</c>. Percent/EarnedPoints/MaxPoints считаются
///     в актуальном режиме знаменателей (choice-only пока AI-грейдинг не READY, потом —
///     включая open_text). <see cref="Weight"/> и <see cref="RecommendedCourseId"/> —
///     снапшот конфига на момент сабмита: нужны для пересчёта тоталов в
///     <see cref="LevelTestAttempt.ApplyAiGrades"/> без повторного похода за answer-key
///     (наружу в DTO не отдаются). Issue #479.
/// </summary>
public sealed record LevelTestSectionScore(
    string Key,
    string Title,
    int Percent,
    string Level,
    decimal EarnedPoints,
    decimal MaxPoints,
    decimal Weight,
    Guid? RecommendedCourseId);
