namespace FileService.Domain;

public sealed class FileStorageRef
{
    private FileStorageRef()
    {
    }

    private FileStorageRef(Guid assetId, StorageKey storageKey)
    {
        AssetId = assetId;
        StorageKey = storageKey;
        Version = Guid.CreateVersion7();
    }

    public Guid AssetId { get; private set; }

    public StorageKey StorageKey { get; private set; }

    public FileStorageMetadata? Metadata { get; private set; }

    public Guid Version { get; private set; }

    public static Result<FileStorageRef, Error> Create(Guid assetId, StorageKey storageKey)
    {
        if (assetId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid("assetId");
        }

        return new FileStorageRef(assetId, storageKey);
    }

    public void UpdateMetadata(FileStorageMetadata metadata)
    {
        Metadata = metadata;
        Version = Guid.CreateVersion7();
    }
}
