using System.Diagnostics.Metrics;

namespace ProgressService.Core.Diagnostics;

/// <summary>
///     Кастомные бизнес-метрики ProgressService (ST-7 #482, паттерн ARS
///     AssignmentReviewMetrics). Регистрация — singleton через <c>IMeterFactory</c>;
///     имя meter'а продублировано константой <c>PROGRESS_METER</c> в
///     <c>Shared.Observability.ObservabilityExtensions</c> (там же <c>.AddMeter(...)</c> —
///     без него OTel pipeline молча дропает инструменты).
/// </summary>
public sealed class ProgressMetrics
{
    public const string METER_NAME = "EducationPlatform.Progress";

    private readonly Counter<long> _levelTestSubmitted;
    private readonly Counter<long> _levelTestClaimed;
    private readonly Counter<long> _levelTestAiGraded;

    public ProgressMetrics(IMeterFactory factory)
    {
        Meter meter = factory.Create(METER_NAME);

        _levelTestSubmitted = meter.CreateCounter<long>(
            name: "level_test_submitted_total",
            unit: "{attempt}",
            description: "Сабмиты попыток level-test'а. Label: subject (anonymous/authenticated).");

        _levelTestClaimed = meter.CreateCounter<long>(
            name: "level_test_claimed_total",
            unit: "{attempt}",
            description: "Анонимные попытки level-test'а, привязанные к юзеру после логина (lead-gate конверсия).");

        _levelTestAiGraded = meter.CreateCounter<long>(
            name: "level_test_ai_graded_total",
            unit: "{attempt}",
            description: "Завершения AI-грейдинга open_text ответов level-test'а. Label: outcome (ready/failed).");
    }

    public void IncrementLevelTestSubmitted(bool authenticated) =>
        _levelTestSubmitted.Add(1,
            new KeyValuePair<string, object?>("subject", authenticated ? "authenticated" : "anonymous"));

    public void IncrementLevelTestClaimed(int claimedCount) =>
        _levelTestClaimed.Add(claimedCount);

    public void IncrementLevelTestAiGraded(bool ready) =>
        _levelTestAiGraded.Add(1,
            new KeyValuePair<string, object?>("outcome", ready ? "ready" : "failed"));
}
