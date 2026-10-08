using System.Diagnostics;
using System.Reflection;

namespace PlatformAuth.Authorization;

/// <summary>
///     Granular domain-level permissions for the platform.
///     Used by endpoints via <c>.RequirePermissions()</c>.
///     Role→permission mapping lives in <see cref="RolePermissions" />.
/// </summary>
public static class PlatformPermissions
{
    public static class Content
    {
        public const string VIEW = "content.view";

        /// <summary>
        ///     Allows staff (moderator) to manage content owned by ANY author —
        ///     bypasses Tier-2 ownership checks in EducationContentService.
        ///     Tier-1 endpoint access still gates on <see cref="Courses.MANAGE" /> /
        ///     <see cref="Issues.MANAGE" />; this permission only lifts the ownership guard.
        /// </summary>
        public const string MODERATE = "content.moderate";
    }

    public static class Courses
    {
        public const string MANAGE = "courses.manage";
    }

    public static class Modules
    {
        public const string MANAGE = "modules.manage";
    }

    public static class Lessons
    {
        public const string MANAGE = "lessons.manage";
    }

    public static class Issues
    {
        public const string MANAGE = "issues.manage";
    }

    public static class Articles
    {
        public const string MANAGE = "articles.manage";
    }

    public static class Files
    {
        public const string UPLOAD = "files.upload";
        public const string MANAGE = "files.manage";
    }

    public static class Videos
    {
        public const string READ = "videos.read";
        public const string MANAGE = "videos.manage";
    }

    public static class Comments
    {
        public const string VIEW = "comments.view";
        public const string WRITE = "comments.write";
        public const string MODERATE = "comments.moderate";
    }

    public static class Users
    {
        public const string VIEW = "users.view";
        public const string MANAGE = "users.manage";
    }

    public static class Progress
    {
        public const string VIEW = "progress.view";
        public const string MANAGE = "progress.manage";
    }

    public static class Profiles
    {
        public const string STUDENT = "profile.student";
        public const string AUTHOR = "profile.author";
        public const string REVIEWER = "profile.reviewer";
    }

    public static class Plans
    {
        public const string MANAGE = "plans.manage";
        public const string GRANT = "plans.grant";
    }

    public static class Platform
    {
        public const string ADMIN = "system.admin";
    }

    /// <summary>
    /// AI-pipeline capability flags. Гейтят привилегированные действия (override
    /// дефолтной модели, impersonation requested-by user), которые исторически
    /// сидели на голом `IsAdmin`. Permission даёт точку для аудита (кто использовал
    /// override) и для расширения круга (модератор / автор) без правки handler'ов.
    /// </summary>
    public static class Ai
    {
        public const string OVERRIDE_MODEL = "ai.override_model";
        public const string IMPERSONATE_REQUESTED_BY = "ai.impersonate_requested_by";
    }

    public static readonly IReadOnlyList<string> All =
    [
        Content.VIEW,
        Content.MODERATE,
        Courses.MANAGE,
        Modules.MANAGE,
        Lessons.MANAGE,
        Issues.MANAGE,
        Articles.MANAGE,
        Files.UPLOAD,
        Files.MANAGE,
        Videos.READ,
        Videos.MANAGE,
        Comments.VIEW,
        Comments.WRITE,
        Comments.MODERATE,
        Users.VIEW,
        Users.MANAGE,
        Progress.VIEW,
        Progress.MANAGE,
        Profiles.STUDENT,
        Profiles.AUTHOR,
        Profiles.REVIEWER,
        Plans.MANAGE,
        Plans.GRANT,
        Platform.ADMIN,
        Ai.OVERRIDE_MODEL,
        Ai.IMPERSONATE_REQUESTED_BY
    ];

#if DEBUG
    static PlatformPermissions()
    {
        var declaredPermissions = typeof(PlatformPermissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var listed = All.ToHashSet();

        var missing = declaredPermissions.Except(listed).ToList();
        var extra = listed.Except(declaredPermissions).ToList();

        Debug.Assert(
            missing.Count == 0,
            $"Permissions.All is missing: {string.Join(", ", missing)}");
        Debug.Assert(
            extra.Count == 0,
            $"Permissions.All has unlisted values: {string.Join(", ", extra)}");
    }
#endif
}
