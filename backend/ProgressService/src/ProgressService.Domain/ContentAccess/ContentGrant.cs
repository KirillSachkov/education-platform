namespace ProgressService.Domain.ContentAccess;

public sealed class ContentGrant
{
    private ContentGrant(
        Guid userId,
        string resourceType,
        Guid resourceId,
        string grantType,
        DateTime? expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        GrantType = grantType;
        GrantedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
        RevokedAt = null;
    }

    // EF Core
    private ContentGrant()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string ResourceType { get; private set; } = null!;

    public Guid ResourceId { get; private set; }

    public string GrantType { get; private set; } = null!;

    public DateTime GrantedAt { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);

    public static Result<ContentGrant, Error> Create(
        Guid userId,
        string resourceType,
        Guid resourceId,
        string grantType,
        DateTime? expiresAt = null)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(userId));

        if (string.IsNullOrWhiteSpace(resourceType))
            return GeneralErrors.ValueIsInvalid(nameof(resourceType));

        if (resourceId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(resourceId));

        if (string.IsNullOrWhiteSpace(grantType))
            return GeneralErrors.ValueIsInvalid(nameof(grantType));

        return new ContentGrant(userId, resourceType, resourceId, grantType, expiresAt);
    }

    public UnitResult<Error> Revoke()
    {
        if (RevokedAt is not null)
            return ProgressErrors.ContentGrantAlreadyRevoked();

        RevokedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }
}
