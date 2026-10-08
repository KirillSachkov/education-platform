namespace FileService.Domain;

public sealed class AssetOwnershipCheckpoint
{
    public AssetOwnershipCheckpoint(
        Guid courseId,
        string targetType,
        Guid targetId,
        Guid desiredOwnerId,
        long lastAppliedRevision)
    {
        CourseId = courseId;
        TargetType = targetType;
        TargetId = targetId;
        DesiredOwnerId = desiredOwnerId;
        LastAppliedRevision = lastAppliedRevision;
        UpdatedAt = DateTime.UtcNow;
    }

    private AssetOwnershipCheckpoint()
    {
    }

    public Guid CourseId { get; }

    public string TargetType { get; } = null!;

    public Guid TargetId { get; }

    public Guid DesiredOwnerId { get; private set; }

    public long LastAppliedRevision { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public void AdvanceTo(long revision, Guid desiredOwnerId)
    {
        if (revision <= LastAppliedRevision)
        {
            return;
        }

        LastAppliedRevision = revision;
        DesiredOwnerId = desiredOwnerId;
        UpdatedAt = DateTime.UtcNow;
    }
}
