using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Domain.AiSettings;

/// <summary>
///     Singleton-row, хранящий effective AI-настройки для трёх pipeline'ов.
///     PK — фиксированный <see cref="SINGLETON_ID"/>; если row отсутствует, resolver
///     возвращает дефолты из appsettings.
/// </summary>
public sealed class AiModelSettings
{
    public static readonly Guid SINGLETON_ID = Guid.Parse("00000000-0000-0000-0000-00000000a1c0");

    private AiModelSettings()
    {
    }

    private AiModelSettings(
        AiModelSlot speechToText,
        AiModelSlot timecodeGeneration,
        AiModelSlot contentGeneration,
        bool autoProcessVideosEnabled,
        Guid updatedByUserId)
    {
        Id = SINGLETON_ID;
        SpeechToText = speechToText;
        TimecodeGeneration = timecodeGeneration;
        ContentGeneration = contentGeneration;
        AutoProcessVideosEnabled = autoProcessVideosEnabled;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    public Guid Id { get; private set; }

    public AiModelSlot SpeechToText { get; private set; } = null!;

    public AiModelSlot TimecodeGeneration { get; private set; } = null!;

    public AiModelSlot ContentGeneration { get; private set; } = null!;

    /// <summary>
    ///     Глобальный тогл авто-обработки видео при готовности (issue #648). Когда
    ///     <c>true</c>, MaterialProcessingService реактивно запускает транскрипцию +
    ///     тайм-коды по событию <c>VideoReadyForProcessing</c>. Default — <c>true</c>.
    ///     Выключается админом через <c>/admin/ai-models</c> без редеплоя (например,
    ///     когда AI-провайдер недоступен или нужно придержать расход).
    /// </summary>
    public bool AutoProcessVideosEnabled { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public static Result<AiModelSettings, Error> Create(
        AiModelSlot speechToText,
        AiModelSlot timecodeGeneration,
        AiModelSlot contentGeneration,
        bool autoProcessVideosEnabled,
        Guid updatedByUserId)
    {
        if (speechToText is null)
            return GeneralErrors.ValueIsRequired(nameof(speechToText));
        if (timecodeGeneration is null)
            return GeneralErrors.ValueIsRequired(nameof(timecodeGeneration));
        if (contentGeneration is null)
            return GeneralErrors.ValueIsRequired(nameof(contentGeneration));
        if (updatedByUserId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(updatedByUserId));

        return new AiModelSettings(
            speechToText,
            timecodeGeneration,
            contentGeneration,
            autoProcessVideosEnabled,
            updatedByUserId);
    }

    public UnitResult<Error> UpdateAll(
        AiModelSlot speechToText,
        AiModelSlot timecodeGeneration,
        AiModelSlot contentGeneration,
        bool autoProcessVideosEnabled,
        Guid updatedByUserId)
    {
        if (speechToText is null)
            return GeneralErrors.ValueIsRequired(nameof(speechToText));
        if (timecodeGeneration is null)
            return GeneralErrors.ValueIsRequired(nameof(timecodeGeneration));
        if (contentGeneration is null)
            return GeneralErrors.ValueIsRequired(nameof(contentGeneration));
        if (updatedByUserId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid(nameof(updatedByUserId));

        SpeechToText = speechToText;
        TimecodeGeneration = timecodeGeneration;
        ContentGeneration = contentGeneration;
        AutoProcessVideosEnabled = autoProcessVideosEnabled;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }
}
