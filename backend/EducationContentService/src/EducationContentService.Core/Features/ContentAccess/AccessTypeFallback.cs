namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     DB-based fallback when Redis key is missing (NOT_SYNCED).
///     Uses access_type already fetched from postgres — zero extra queries.
///     FREE/ENROLLED fail-closed because user enrollment can't be verified without Redis.
/// </summary>
public static class AccessTypeFallback
{
    public static bool IsAllowed(string accessType, bool isAuthenticated) =>
        string.Equals(accessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(accessType, "REGISTERED", StringComparison.OrdinalIgnoreCase) && isAuthenticated);
}
