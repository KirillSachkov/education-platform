using MaterialProcessingService.Contracts.AiSettings.Dtos;

namespace MaterialProcessingService.Contracts.AiSettings.Requests;

public sealed record UpdateAiModelSettingsRequest(
    AiModelSlotDto SpeechToText,
    AiModelSlotDto TimecodeGeneration,
    AiModelSlotDto ContentGeneration,
    bool AutoProcessVideosEnabled = true);
