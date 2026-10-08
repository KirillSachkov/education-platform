using System.Diagnostics.Metrics;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Diagnostics;

/// <summary>
///     Phase 12 (#15): кастомные бизнес-метрики ARS. Регистрация — через
///     <c>IMeterFactory</c> + global meter constant <c>ASSIGNMENT_REVIEW_SOURCE</c>
///     в <c>Shared.Observability.ObservabilityExtensions</c> (singleton).
/// </summary>
public sealed class AssignmentReviewMetrics
{
    public const string METER_NAME = "EducationPlatform.AssignmentReview";

    private readonly Histogram<double> _iterationDuration;
    private readonly Histogram<long> _diffAdditions;
    private readonly Counter<long> _iterationOutcomes;
    private readonly Counter<long> _githubReviewsPosted;
    private readonly Counter<long> _iterationFeedback;
    private readonly Counter<long> _repoContextRounds;
    private readonly Counter<long> _repoContextFiles;

    // #690 — последнее наблюдённое stale-watchdog'ом число зависших ревью (QUEUED/RUNNING
    // дольше порога), переотправленных в последнем проходе. Кормится из
    // StaleRunningReviewRecoveryService; ObservableGauge отдаёт его при scrape'е.
    private long _stuckReviews;

    public AssignmentReviewMetrics(IMeterFactory factory)
    {
        Meter meter = factory.Create(METER_NAME);

        meter.CreateObservableGauge(
            name: "assignment_review_stuck_reviews",
            observeValue: () => Interlocked.Read(ref _stuckReviews),
            unit: "{review}",
            description: "Зависшие AI-ревью (QUEUED/RUNNING дольше порога), найденные и переотправленные последним проходом stale-watchdog'а (#690). >0 устойчиво = что-то ломает прогон ревью.");

        _iterationDuration = meter.CreateHistogram<double>(
            name: "assignment_review_iteration_duration_seconds",
            unit: "s",
            description: "End-to-end длительность одного run-iteration (от accept до persist).");

        _diffAdditions = meter.CreateHistogram<long>(
            name: "assignment_review_diff_additions",
            unit: "{lines}",
            description: "Количество добавленных строк в diff'е, отправленном LLM.");

        _iterationOutcomes = meter.CreateCounter<long>(
            name: "assignment_review_iteration_outcomes_total",
            unit: "{iteration}",
            description: "Outcomes run-iteration: verdict (LOOKS_GOOD/MINOR/MAJOR/OFF_TOPIC) или failure code.");

        _githubReviewsPosted = meter.CreateCounter<long>(
            name: "assignment_review_github_reviews_posted_total",
            unit: "{review}",
            description: "Сколько GitHub PR review'ов реально отправлено (skip'ает no-op LOOKS_GOOD без коммёнтов).");

        _iterationFeedback = meter.CreateCounter<long>(
            name: "assignment_review_iteration_feedback_total",
            unit: "{feedback}",
            description: "Submitted author feedback на AI iteration. Labels: verdict (verdict iteration'а), helpful (true/false).");

        _repoContextRounds = meter.CreateCounter<long>(
            name: "assignment_review_repo_context_rounds_total",
            unit: "{round}",
            description: "Дополнительные LLM-раунды цикла дозапроса файлов репозитория (#798). Отношение к iteration_outcomes_total даёт долю ревью с дозапросом.");

        _repoContextFiles = meter.CreateCounter<long>(
            name: "assignment_review_repo_context_files_total",
            unit: "{file}",
            description: "Файлы репозитория, дозапрошенные ревьюером через need_files (#798).");
    }

    public void RecordIterationDuration(TimeSpan elapsed, string outcome) =>
        _iterationDuration.Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordDiffAdditions(int additions) =>
        _diffAdditions.Record(additions);

    public void IncrementIterationOutcome(AiReviewVerdict? verdict, string? failureCode)
    {
        string outcome = verdict?.ToString() ?? failureCode ?? "UNKNOWN";
        _iterationOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void IncrementGitHubReviewPosted() =>
        _githubReviewsPosted.Add(1);

    public void IncrementIterationFeedback(string verdict, bool helpful) =>
        _iterationFeedback.Add(1,
            new KeyValuePair<string, object?>("verdict", verdict),
            new KeyValuePair<string, object?>("helpful", helpful.ToString().ToLowerInvariant()));

    /// <summary>#798 — телеметрия завершённого цикла дозапроса файлов (extraRounds > 0).</summary>
    public void RecordRepoContextLoop(int extraRounds, int requestedFiles)
    {
        if (extraRounds > 0)
            _repoContextRounds.Add(extraRounds);
        if (requestedFiles > 0)
            _repoContextFiles.Add(requestedFiles);
    }

    /// <summary>
    ///     #690 — записывает число зависших ревью, найденных stale-watchdog'ом в последнем
    ///     проходе (gauge <c>assignment_review_stuck_reviews</c>). Вызывается каждый тик
    ///     <c>StaleRunningReviewRecoveryService</c>, включая 0 (чтобы gauge спадал, когда
    ///     застрявших не осталось).
    /// </summary>
    public void SetStuckReviewCount(long count) =>
        Interlocked.Exchange(ref _stuckReviews, count);
}
