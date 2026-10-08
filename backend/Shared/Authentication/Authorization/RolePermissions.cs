namespace PlatformAuth.Authorization;

public static class RolePermissions
{
    private static class Sets
    {
        public static readonly string[] ContentManage =
        [
            PlatformPermissions.Courses.MANAGE,
            PlatformPermissions.Modules.MANAGE,
            PlatformPermissions.Lessons.MANAGE,
            PlatformPermissions.Issues.MANAGE,
            PlatformPermissions.Articles.MANAGE
        ];

        public static readonly string[] MediaManage =
        [
            PlatformPermissions.Files.UPLOAD,
            PlatformPermissions.Files.MANAGE,
            PlatformPermissions.Videos.READ,
            PlatformPermissions.Videos.MANAGE
        ];

        public static readonly string[] UserManage =
        [
            PlatformPermissions.Users.VIEW,
            PlatformPermissions.Users.MANAGE
        ];
    }

    private static readonly IReadOnlyDictionary<string, HashSet<string>> _mapping = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        [PlatformRoles.PARTICIPANT] =
        [
            PlatformPermissions.Content.VIEW,
            PlatformPermissions.Comments.VIEW,
            PlatformPermissions.Comments.WRITE,
            PlatformPermissions.Progress.VIEW,
            PlatformPermissions.Files.UPLOAD,
            PlatformPermissions.Profiles.STUDENT
        ],
        [PlatformRoles.AUTHOR] =
        [
            PlatformPermissions.Content.VIEW,
            PlatformPermissions.Comments.VIEW,
            PlatformPermissions.Comments.WRITE,
            PlatformPermissions.Progress.VIEW,
            .. Sets.ContentManage,
            .. Sets.MediaManage,
            PlatformPermissions.Plans.MANAGE,
            PlatformPermissions.Plans.GRANT,
            PlatformPermissions.Profiles.AUTHOR
        ],
        [PlatformRoles.EDITOR] =
        [
            PlatformPermissions.Content.VIEW,
            PlatformPermissions.Content.MODERATE,
            PlatformPermissions.Comments.VIEW,
            PlatformPermissions.Comments.WRITE,
            PlatformPermissions.Progress.VIEW,
            .. Sets.ContentManage,
            .. Sets.MediaManage,
            PlatformPermissions.Profiles.AUTHOR
        ],
        [PlatformRoles.MODERATOR] =
        [
            PlatformPermissions.Content.VIEW,
            PlatformPermissions.Content.MODERATE,
            PlatformPermissions.Comments.VIEW,
            PlatformPermissions.Comments.WRITE,
            PlatformPermissions.Comments.MODERATE,
            PlatformPermissions.Progress.VIEW,
            PlatformPermissions.Progress.MANAGE,
            .. Sets.ContentManage,
            .. Sets.MediaManage,
            .. Sets.UserManage,
            PlatformPermissions.Profiles.REVIEWER
        ],
        [PlatformRoles.ADMIN] = [.. PlatformPermissions.All],
        [PlatformRoles.OWNER] = [.. PlatformPermissions.All],
        [PlatformRoles.SERVICE] = [.. PlatformPermissions.All]
    };

    public static IReadOnlySet<string> GetPermissions(IEnumerable<string> roles)
    {
        HashSet<string> permissions = [];

        foreach (string role in roles)
        {
            if (_mapping.TryGetValue(role, out HashSet<string>? rolePerms))
            {
                permissions.UnionWith(rolePerms);
            }
        }

        return permissions;
    }
}
