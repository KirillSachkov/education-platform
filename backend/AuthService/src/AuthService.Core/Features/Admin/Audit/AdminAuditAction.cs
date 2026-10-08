namespace AuthService.Core.Features.Admin.Audit;

public static class AdminAuditAction
{
    public const string UserCreated = "users.created";
    public const string UserUpdated = "users.updated";
    public const string UserDeleted = "users.deleted";
    public const string UserLockoutSet = "users.lockout.set";
    public const string UserPasswordSet = "users.password.set";
    public const string UserRolesSet = "users.roles.set";
    public const string UserBulkLockout = "users.bulk.lockout";
    public const string UserBulkRoles = "users.bulk.roles";
    public const string UsersExportCsv = "users.export.csv";
}
