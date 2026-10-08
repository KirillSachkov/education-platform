namespace FileService.Domain;

public sealed class VideoProviderRef
{
    private VideoProviderRef()
    {
    }

    private VideoProviderRef(Guid assetId, AssetProviderType providerCode, string externalAssetId)
    {
        AssetId = assetId;
        ProviderCode = providerCode;
        ExternalAssetId = externalAssetId;
        Version = Guid.CreateVersion7();
    }

    public Guid AssetId { get; private set; }

    public AssetProviderType ProviderCode { get; private set; }

    public string ExternalAssetId { get; private set; } = string.Empty;

    public VideoProviderMetadata? Metadata { get; private set; }

    public Guid Version { get; private set; }

    public static Result<VideoProviderRef, Error> Create(
        Guid assetId,
        AssetProviderType providerCode,
        string externalAssetId)
    {
        if (assetId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("assetId");
        }

        if (string.IsNullOrWhiteSpace(externalAssetId))
        {
            return GeneralErrors.ValueIsInvalid("externalAssetId");
        }

        return new VideoProviderRef(assetId, providerCode, externalAssetId.Trim());
    }

    public void UpdateMetadata(VideoProviderMetadata metadata)
    {
        Metadata = metadata;
        Version = Guid.CreateVersion7();
    }
}
