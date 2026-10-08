using FileService.Core.Messaging;
using FileService.Core.Services.Files;
using FileService.Domain;
using SharedKernel.Exceptions;

namespace FileService.Core.Features.Files.IntegrationEvents;

/// <summary>
///     Self-consumed handler for <see cref="GenerateImageVariants"/> (issue #646).
///     Runs asynchronously off the upload/bind hot path so generation never blocks
///     <c>POST /files/{id}/complete</c>. Idempotent — safe to re-deliver.
/// </summary>
public sealed class GenerateImageVariantsHandler
{
    // Renderer error codes that can NEVER succeed on retry: an unsupported/corrupt image,
    // or an over-budget one. Wrapping these in TransientException would burn 4 attempts +
    // 4 S3 downloads per poison image; PermanentException → DLQ immediately instead.
    private static readonly HashSet<string> _permanentErrorCodes = new(StringComparer.Ordinal)
    {
        ImageVariantPolicy.ERROR_DECODE_FAILED,
        ImageVariantPolicy.ERROR_TOO_LARGE,
    };

    private readonly ImageVariantGenerationService _generationService;
    private readonly ILogger<GenerateImageVariantsHandler> _logger;

    public GenerateImageVariantsHandler(
        ImageVariantGenerationService generationService,
        ILogger<GenerateImageVariantsHandler> logger)
    {
        _generationService = generationService;
        _logger = logger;
    }

    public async Task Handle(GenerateImageVariants message, CancellationToken cancellationToken)
    {
        Result<int, Error> result = await _generationService.GenerateAsync(message.AssetId, cancellationToken);
        if (result.IsFailure)
        {
            // Surface as an exception so Wolverine's standard error policy applies. A
            // malformed, unsupported, or oversized image is permanent and goes straight to
            // the dead-letter queue with no retries; genuine infra blips such as an S3
            // download or IO error stay transient and are retried, then dead-lettered on
            // exhaustion. See Core/Messaging/WolverineErrorHandlingExtensions.cs for policy.
            bool isPermanent = result.Error.Messages.Any(m => _permanentErrorCodes.Contains(m.Code));

            _logger.LogWarning(
                "GenerateImageVariantsHandler: generation failed for asset {AssetId}: {ErrorType} (permanent={IsPermanent})",
                message.AssetId, result.Error.Type, isPermanent);

            if (isPermanent)
            {
                throw new PermanentException(result.Error);
            }

            throw new TransientException(result.Error);
        }
    }
}
