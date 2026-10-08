using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Infrastructure.AI.Configuration;

namespace MaterialProcessingService.Infrastructure.AI.AiSettings;

internal static class EffectiveAiModelSlotExtensions
{
    /// <summary>
    ///     Adapter: переводит provider-agnostic <see cref="EffectiveAiModelSlot"/> в
    ///     <see cref="VideoProcessingAiModelOptions"/>, который ожидают request-factories.
    /// </summary>
    public static VideoProcessingAiModelOptions ToOptions(this EffectiveAiModelSlot slot) =>
        new()
        {
            Model = slot.Model,
            Provider = slot.Provider,
            Temperature = slot.Temperature,
            MaxOutputTokens = slot.MaxOutputTokens,
            TimeoutSeconds = slot.TimeoutSeconds,
        };
}
