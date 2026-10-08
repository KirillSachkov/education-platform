namespace MaterialProcessingService.Contracts.AiSettings.Dtos;

public sealed record AiModelSettingsDto(
    AiModelSlotDto SpeechToText,
    AiModelSlotDto TimecodeGeneration,
    AiModelSlotDto ContentGeneration,
    bool AutoProcessVideosEnabled,
    string Source,
    string? UpdatedAtUtc,
    string? UpdatedByUserId);

public sealed record AiModelSlotDto(
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds);
