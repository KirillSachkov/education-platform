namespace ContentAccess.Redis;

public static class EntitlementKeys
{
    public static string ResourceAccess(string resourceType, Guid resourceId)
        => $"resource-access:{resourceType}:{resourceId:D}";

    public static string UserGrants(Guid userId)
        => $"user-grants:{userId:D}";
}
