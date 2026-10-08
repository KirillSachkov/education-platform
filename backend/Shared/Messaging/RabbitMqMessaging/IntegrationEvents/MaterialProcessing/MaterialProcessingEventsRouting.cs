namespace Shared.Messaging.IntegrationEvents.MaterialProcessing;

/// <summary>
///     Routing для исходящих событий MaterialProcessingService. Первый (и пока единственный)
///     publisher-канал сервиса — раньше он только consume'ил. Issue #648.
/// </summary>
public static class MaterialProcessingEventsRouting
{
    public const string EXCHANGE = "material_processing.events";

    public static class RoutingKeys
    {
        public const string VIDEO_AUTO_PROCESSING_FAILED = "video.auto_processing.failed";
    }
}
