namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Routing keys для exchange <c>assignment_review.events</c> (Phase 7+, issue #15).
///     Consumer: ProgressService (Phase 8 — denormы на issue_submission'е).
/// </summary>
public static class AssignmentReviewEventsRouting
{
    public const string EXCHANGE = "assignment_review.events";

    public static class RoutingKeys
    {
        public static string IterationCompleted() => "ai_review.iteration.completed";
        public static string QueuedForSubmission() => "ai_review.queued_for_submission";

        /// <summary>
        ///     Issue #307: VcsInstallation создан / переустановлен / реактивирован.
        ///     Consumer — AccessService (auto-complete GITHUB_REVIEW_APP onboarding step).
        /// </summary>
        public static string VcsInstallationCreated() => "vcs_installation.created";

        /// <summary>
        ///     Issue #546: авто-ран AI-проверки пропущен — reviewable diff выше hard-cap'ов.
        ///     Consumer — NotificationService (уведомление автору курса «запустите вручную»).
        /// </summary>
        public static string OversizedSkipped() => "ai_review.oversized_skipped";

        /// <summary>
        ///     Issue #713: студент прокомментировал свой PR (обратный канал к AI-ревью).
        ///     Consumers — NotificationService (уведомление автору курса) + ProgressService.
        /// </summary>
        public static string StudentPrQuestionAsked() => "student_pr_question.asked";
    }
}
