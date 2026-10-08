namespace FileService.Domain;

public static class KinescopeStatusMapper
{
    public static AssetStatus MapToAssetStatus(string providerStatus) =>
        providerStatus.Trim().ToLowerInvariant() switch
        {
            "done" => AssetStatus.READY,
            "error" or "aborted" => AssetStatus.FAILED,
            "uploading" => AssetStatus.PENDING_UPLOAD,
            _ => AssetStatus.PROCESSING,
        };
}
