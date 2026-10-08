namespace FileService.Domain;

public enum AssetStaleWorkflowType
{
    PendingFileUpload = 1,
    DraftFile = 2,
    VideoUpload = 3,
    VideoProcessing = 4,
    Delete = 5,
}
