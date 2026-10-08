namespace FileService.Domain;

public enum AssetStatus
{
    PENDING_UPLOAD = 1,
    PROCESSING = 2,
    READY = 3,
    FAILED = 4,
    DELETING = 5,
    DELETED = 6,
}
