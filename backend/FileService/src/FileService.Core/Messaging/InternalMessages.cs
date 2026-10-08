namespace FileService.Core.Messaging;

/// <summary>
///     FileService-internal command (issue #646): generate responsive image variants for
///     a Ready image asset. Published via the outbox on complete + bind, self-consumed on a
///     local Wolverine queue (never crosses the broker). The handler is idempotent.
/// </summary>
public sealed record GenerateImageVariants(Guid AssetId);
