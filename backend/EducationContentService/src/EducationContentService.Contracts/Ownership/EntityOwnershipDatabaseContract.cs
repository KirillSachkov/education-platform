namespace EducationContentService.Contracts.Ownership;

/// <summary>
///     Versioned read-only database contract for consumers that require ownership and
///     comment rows in one PostgreSQL snapshot. ECS owns the view definition and keeps v1
///     stable; breaking ownership/schema changes require a new version and consumer rollout.
/// </summary>
public static class EntityOwnershipDatabaseContract
{
    public const string COMMENT_TARGET_OWNERSHIP_VIEW_V1 =
        "education.comment_target_ownership_v1";
}