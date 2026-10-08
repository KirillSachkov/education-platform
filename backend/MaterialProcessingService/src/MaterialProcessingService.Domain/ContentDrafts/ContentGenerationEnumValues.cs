namespace MaterialProcessingService.Domain.ContentDrafts;

public static class ContentGenerationEnumValues
{
    public static string ToStorageValue(this ContentGenerationStatus status) =>
        status switch
        {
            ContentGenerationStatus.Queued => "QUEUED",
            ContentGenerationStatus.Processing => "PROCESSING",
            ContentGenerationStatus.Completed => "COMPLETED",
            ContentGenerationStatus.Failed => "FAILED",
            _ => status.ToString()
        };

    public static string ToStorageValue(this ContentGenerationStage stage) =>
        stage switch
        {
            ContentGenerationStage.Queued => "QUEUED",
            ContentGenerationStage.SourceFetch => "SOURCE_FETCH",
            ContentGenerationStage.Probe => "PROBE",
            ContentGenerationStage.AudioExtract => "AUDIO_EXTRACT",
            ContentGenerationStage.Transcribe => "TRANSCRIBE",
            ContentGenerationStage.Generate => "GENERATE",
            ContentGenerationStage.Save => "SAVE",
            _ => stage.ToString()
        };

    public static ContentGenerationStatus ToContentGenerationStatus(string value) =>
        value.ToUpperInvariant() switch
        {
            "QUEUED" => ContentGenerationStatus.Queued,
            "PROCESSING" => ContentGenerationStatus.Processing,
            "COMPLETED" => ContentGenerationStatus.Completed,
            "FAILED" => ContentGenerationStatus.Failed,
            _ => Enum.Parse<ContentGenerationStatus>(value, ignoreCase: true)
        };

    public static ContentGenerationStage ToContentGenerationStage(string value) =>
        value.ToUpperInvariant() switch
        {
            "QUEUED" => ContentGenerationStage.Queued,
            "SOURCE_FETCH" => ContentGenerationStage.SourceFetch,
            "PROBE" => ContentGenerationStage.Probe,
            "AUDIO_EXTRACT" => ContentGenerationStage.AudioExtract,
            "TRANSCRIBE" => ContentGenerationStage.Transcribe,
            "GENERATE" => ContentGenerationStage.Generate,
            "SAVE" => ContentGenerationStage.Save,
            _ => Enum.Parse<ContentGenerationStage>(value, ignoreCase: true)
        };

}
