namespace FileService.Domain;

public static class AssetStatusExtensions
{
    public static string ToApiString(this AssetStatus status) =>
        status switch
        {
            AssetStatus.PENDING_UPLOAD => "pending_upload",
            AssetStatus.PROCESSING => "processing",
            AssetStatus.READY => "ready",
            AssetStatus.FAILED => "failed",
            AssetStatus.DELETING => "deleting",
            AssetStatus.DELETED => "deleted",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
