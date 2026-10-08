using System.Diagnostics.Metrics;

namespace EducationContentService.Core.Diagnostics;

/// <summary>
///     Кастомные бизнес-метрики ECS (ST-7 #482, паттерн ARS AssignmentReviewMetrics).
///     Регистрация — singleton через <c>IMeterFactory</c>; имя meter'а продублировано
///     константой <c>EDUCATION_METER</c> в <c>Shared.Observability.ObservabilityExtensions</c>
///     (там же <c>.AddMeter(...)</c> — без него OTel pipeline молча дропает инструменты).
/// </summary>
public sealed class EducationContentMetrics
{
    public const string METER_NAME = "EducationPlatform.Education";

    private readonly Counter<long> _levelTestFetched;

    public EducationContentMetrics(IMeterFactory factory)
    {
        Meter meter = factory.Create(METER_NAME);

        _levelTestFetched = meter.CreateCounter<long>(
            name: "level_test_fetched_total",
            unit: "{fetch}",
            description: "Успешные выдачи активного level-test'а (GetActiveLevelTest) — верх воронки, proxy «начали тест».");
    }

    public void IncrementLevelTestFetched() =>
        _levelTestFetched.Add(1);
}
