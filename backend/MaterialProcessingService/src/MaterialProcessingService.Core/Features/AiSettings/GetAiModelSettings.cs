using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using MaterialProcessingService.Contracts.AiSettings.Dtos;
using MaterialProcessingService.Core.AiSettings;

namespace MaterialProcessingService.Core.Features.AiSettings;

public sealed class GetAiModelSettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // EndpointResult<T> — Framework оборачивает payload в Envelope {result,error,isError,timeGenerated}
        // (как все остальные endpoint'ы). Без envelope frontend `materialProcessingApi.getAiModelSettings`
        // делает `res.data.result!` → undefined → `/admin/ai-models` бесконечно грузится (issue #185).
        app.MapGet("/material-processing/admin/ai-settings/",
                async Task<EndpointResult<AiModelSettingsDto>> (
                    [FromServices] IAiModelSettingsResolver resolver,
                    CancellationToken cancellationToken) =>
                {
                    EffectiveAiModelSettings settings = await resolver.GetAsync(cancellationToken);
                    return MapToDto(settings);
                })
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }

    internal static AiModelSettingsDto MapToDto(EffectiveAiModelSettings settings) => new(
        SpeechToText: ToSlotDto(settings.SpeechToText),
        TimecodeGeneration: ToSlotDto(settings.TimecodeGeneration),
        ContentGeneration: ToSlotDto(settings.ContentGeneration),
        AutoProcessVideosEnabled: settings.AutoProcessVideosEnabled,
        Source: settings.Source.ToString(),
        UpdatedAtUtc: settings.UpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        UpdatedByUserId: settings.UpdatedByUserId?.ToString());

    private static AiModelSlotDto ToSlotDto(EffectiveAiModelSlot slot) =>
        new(slot.Model, slot.Temperature, slot.MaxOutputTokens, slot.TimeoutSeconds);
}
