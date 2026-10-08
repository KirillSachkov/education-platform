namespace Shared.Messaging.IntegrationEvents.Education;

public static class EducationEventsRouting
{
    public const string EXCHANGE = "education.events";

    public static class RoutingKeys
    {
        public const string ALL_HARD_DELETED = "*.hard_deleted";
        public const string ALL_CREATED = "*.created";
        public const string ALL_SOFT_DELETED = "*.soft_deleted";
        public const string ALL_RESTORED = "*.restored";
        public const string ALL_UPDATED = "*.updated";
        public const string ALL_PUBLISHED = "*.published";

        public static string IssueCreated() => "issue.created";
        public static string IssueUpdated() => "issue.updated";
        public static string IssueAccessChanged() => "issue.access_changed";
        public static string IssuePublished() => "issue.published";
        public static string IssueSoftDeleted() => "issue.soft_deleted";
        public static string IssueRestored() => "issue.restored";
        public static string IssueHardDeleted() => "issue.hard_deleted";

        public static string MaterialCreated() => "material.created";
        public static string MaterialUpdated() => "material.updated";
        public static string MaterialPublished() => "material.published";
        public static string MaterialSentToDraft() => "material.sent_to_draft";
        public static string MaterialArchived() => "material.archived";
        public static string MaterialAccessChanged() => "material.access_changed";
        public static string MaterialHardDeleted() => "material.hard_deleted";
        public static string MaterialBindDraftAssets() => "material.bind_draft_assets";

        public static string ModuleCreated() => "module.created";
        public static string ModuleUpdated() => "module.updated";
        public static string ModulePublished() => "module.published";
        public static string ModuleSoftDeleted() => "module.soft_deleted";
        public static string ModuleRestored() => "module.restored";
        public static string ModuleHardDeleted() => "module.hard_deleted";

        public static string ProjectCreated() => "project.created";
        public static string ProjectUpdated() => "project.updated";
        public static string ProjectPublished() => "project.published";
        public static string ProjectSoftDeleted() => "project.soft_deleted";
        public static string ProjectRestored() => "project.restored";

        // Phase 5 (#15) — AssignmentReviewService config.
        public static string ProjectReviewContextUpdated() => "project.review_context.updated";
        public static string IssueReviewSpecUpdated() => "issue.review_spec.updated";

        public static string CourseCreated() => "course.created";
        public static string CourseUpdated() => "course.updated";
        public static string CoursePublished() => "course.published";
        public static string CourseSoftDeleted() => "course.soft_deleted";
        public static string CourseRestored() => "course.restored";
        public static string CourseHardDeleted() => "course.hard_deleted";
        public static string CourseAssetOwnershipChanged() => "course.asset_ownership_changed";

        public static string QuizPublished() => "quiz.published";
        public static string QuizAccessChanged() => "quiz.access_changed";
        public static string QuizHardDeleted() => "quiz.hard_deleted";

        public static string CollectionCreated() => "collection.created";
        public static string CollectionUpdated() => "collection.updated";
        public static string CollectionPublished() => "collection.published";
        public static string CollectionAccessChanged() => "collection.access_changed";
        public static string CollectionHardDeleted() => "collection.hard_deleted";

        public static string EntityHardDeleted(string entityType) => $"{entityType}.hard_deleted";

    }
}
