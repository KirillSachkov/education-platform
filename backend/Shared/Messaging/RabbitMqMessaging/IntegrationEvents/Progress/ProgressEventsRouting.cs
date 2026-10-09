namespace Shared.Messaging.IntegrationEvents.Progress;

public static class ProgressEventsRouting
{
    public const string EXCHANGE = "progress.events";

    public static class RoutingKeys
    {
        public static string IssueSubmissionApproved() => "issue_submission.approved";
        public static string IssueSubmissionChangesRequested() => "issue_submission.changes_requested";
        public static string IssueSubmissionAwaitingReview() => "issue_submission.awaiting_review";
        public static string IssueSubmissionAuthorHelpRequested() => "issue_submission.author_help_requested";
        public static string IssueAuthorQuestionAsked() => "issue.author_question_asked";
    }
}