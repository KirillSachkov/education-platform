namespace MaterialProcessingService.Domain.Timecodes;

public static class TimecodeEnumValues
{
    public static string ToStorageValue(this TimecodeGenerationStatus status) =>
        status switch
        {
            TimecodeGenerationStatus.Queued => "QUEUED",
            TimecodeGenerationStatus.Processing => "PROCESSING",
            TimecodeGenerationStatus.Completed => "COMPLETED",
            TimecodeGenerationStatus.Failed => "FAILED",
            _ => status.ToString()
        };

    public static string ToStorageValue(this TimecodeGenerationStage stage) =>
        stage switch
        {
            TimecodeGenerationStage.Queued => "QUEUED",
            TimecodeGenerationStage.SourceFetch => "SOURCE_FETCH",
            TimecodeGenerationStage.Probe => "PROBE",
            TimecodeGenerationStage.AudioExtract => "AUDIO_EXTRACT",
            TimecodeGenerationStage.Transcribe => "TRANSCRIBE",
            TimecodeGenerationStage.Generate => "GENERATE",
            TimecodeGenerationStage.Save => "SAVE",
            _ => stage.ToString()
        };

    public static TimecodeGenerationStatus ToTimecodeGenerationStatus(string value) =>
        value.ToUpperInvariant() switch
        {
            "QUEUED" => TimecodeGenerationStatus.Queued,
            "PROCESSING" => TimecodeGenerationStatus.Processing,
            "COMPLETED" => TimecodeGenerationStatus.Completed,
            "FAILED" => TimecodeGenerationStatus.Failed,
            _ => Enum.Parse<TimecodeGenerationStatus>(value, ignoreCase: true)
        };

    public static TimecodeGenerationStage ToTimecodeGenerationStage(string value) =>
        value.ToUpperInvariant() switch
        {
            "QUEUED" => TimecodeGenerationStage.Queued,
            "SOURCE_FETCH" => TimecodeGenerationStage.SourceFetch,
            "PROBE" => TimecodeGenerationStage.Probe,
            "AUDIO_EXTRACT" => TimecodeGenerationStage.AudioExtract,
            "TRANSCRIBE" => TimecodeGenerationStage.Transcribe,
            "GENERATE" => TimecodeGenerationStage.Generate,
            "SAVE" => TimecodeGenerationStage.Save,
            _ => Enum.Parse<TimecodeGenerationStage>(value, ignoreCase: true)
        };

}
