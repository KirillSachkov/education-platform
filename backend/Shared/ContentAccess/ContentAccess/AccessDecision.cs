namespace ContentAccess;

public sealed record AccessDecision(bool IsGranted, AccessReason Reason)
{
    public static AccessDecision Granted(AccessReason reason) => new(true, reason);
    public static AccessDecision Denied() => new(false, AccessReason.NONE);
    public static AccessDecision Denied(AccessReason reason) => new(false, reason);
}

public enum AccessReason
{
    NONE,
    ADMIN_OR_AUTHOR,
    ENTITLEMENT,
    AUTHENTICATED_ONLY,
    PUBLIC,
    NOT_AUTHENTICATED,
    RESOURCE_NOT_REGISTERED,
}
