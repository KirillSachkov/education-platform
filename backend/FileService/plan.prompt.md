# Kinescope Integration Plan

## Архитектура

### Принцип работы

1. **Клиент** отправляет `InitiateUploadRequest` с `AssetType`
2. **FileService** по `AssetType` определяет провайдер (VIDEO → Kinescope, PREVIEW/SCREENSHOT → S3)
3. **Ответ** содержит `StorageProvider` enum + заполненный объект данных (S3 или Kinescope)
4. **Клиент** проверяет enum и использует соответствующие данные для загрузки
5. **Завершение**: для видео — webhook обновляет статус; для S3 — клиент вызывает CompleteUpload

---

## 1. Contracts

### StorageProvider Enum

```csharp
// FileService.Contracts/StorageProvider.cs
public enum StorageProvider
{
    S3,
    Kinescope
}
```

### Upload Response

```csharp
// FileService.Contracts/Responses/InitiateUploadResponse.cs
public record InitiateUploadResponse(
    Guid AssetId,
    StorageProvider Provider,
    S3UploadData? S3,
    KinescopeUploadData? Kinescope);

public record S3UploadData(
    string UploadId,
    List<ChunkUploadUrl> ChunkUrls);

public record ChunkUploadUrl(int PartNumber, string Url);

public record KinescopeUploadData(
    string VideoId,
    string UploadLink);  // TUS endpoint
```

### GetMediaAsset Response

```csharp
// FileService.Contracts/Responses/GetMediaAssetResponse.cs
public record GetMediaAssetResponse(
    Guid Id,
    string AssetType,
    string Status,
    StorageProvider Provider,
    S3AccessData? S3,
    KinescopeAccessData? Kinescope);

public record S3AccessData(string Url);

public record KinescopeAccessData(
    string EmbedUrl,
    string? HlsLink);
```

---

## 2. Core Layer — Providers

### IUploadProvider Interface

```csharp
// FileService.Core/Providers/IUploadProvider.cs
public interface IUploadProvider
{
    StorageBackend Backend { get; }

    Task<Result<UploadInitResult, Error>> InitiateUploadAsync(
        MediaAsset asset,
        CancellationToken ct);
}

public abstract record UploadInitResult;

public record S3UploadInitResult(
    string UploadId,
    List<(int PartNumber, string Url)> ChunkUrls) : UploadInitResult;

public record KinescopeUploadInitResult(
    string VideoId,
    string UploadLink) : UploadInitResult;
```

### IStorageAccessProvider Interface

```csharp
// FileService.Core/Providers/IStorageAccessProvider.cs
public interface IStorageAccessProvider
{
    StorageBackend Backend { get; }

    Task<Result<AccessInfo, Error>> GetAccessInfoAsync(
        MediaAsset asset,
        CancellationToken ct);
}

public abstract record AccessInfo;

public record S3AccessInfo(string Url) : AccessInfo;

public record KinescopeAccessInfo(
    string EmbedUrl,
    string? HlsLink) : AccessInfo;
```

### Resolver Classes

```csharp
// FileService.Core/Providers/UploadProviderResolver.cs
public class UploadProviderResolver(IEnumerable<IUploadProvider> providers)
{
    public IUploadProvider Resolve(StorageBackend backend) =>
        providers.FirstOrDefault(p => p.Backend == backend)
            ?? throw new InvalidOperationException($"No provider for {backend}");
}

// FileService.Core/Providers/StorageAccessProviderResolver.cs
public class StorageAccessProviderResolver(IEnumerable<IStorageAccessProvider> providers)
{
    public IStorageAccessProvider Resolve(StorageBackend backend) =>
        providers.FirstOrDefault(p => p.Backend == backend)
            ?? throw new InvalidOperationException($"No provider for {backend}");
}
```

### Provider Implementations

```csharp
// FileService.Core/Providers/S3UploadProvider.cs
public class S3UploadProvider(IS3Provider s3) : IUploadProvider
{
    public StorageBackend Backend => StorageBackend.S3;

    public async Task<Result<UploadInitResult, Error>> InitiateUploadAsync(
        MediaAsset asset, CancellationToken ct)
    {
        var storage = (S3StorageReference)asset.StorageReference;
        var uploadId = await s3.StartMultipartUploadAsync(storage.FullPath, ct);
        var urls = await s3.GeneratePresignedUrls(uploadId, storage.FullPath, ...);
        return new S3UploadInitResult(uploadId, urls);
    }
}

// FileService.Core/Providers/KinescopeUploadProvider.cs
public class KinescopeUploadProvider(IKinescopeApiClient kinescope) : IUploadProvider
{
    public StorageBackend Backend => StorageBackend.Kinescope;

    public async Task<Result<UploadInitResult, Error>> InitiateUploadAsync(
        MediaAsset asset, CancellationToken ct)
    {
        var result = await kinescope.InitializeUploadAsync(asset.FileName, ct);
        return new KinescopeUploadInitResult(result.VideoId, result.UploadLink);
    }
}
```

---

## 3. Features

### InitiateUploadHandler (Refactor)

```csharp
// FileService.Core/Features/InitiateUpload/InitiateUploadHandler.cs
public async Task<Result<InitiateUploadResponse, Error>> Handle(
    InitiateUploadRequest request, CancellationToken ct)
{
    // 1. Create asset (factory determines storage backend)
    var asset = MediaAssetFactory.Create(request.AssetType, request.FileName, ...);

    // 2. Resolve provider by asset's storage backend
    var provider = _uploadProviderResolver.Resolve(asset.StorageReference.Backend);

    // 3. Initiate upload
    var initResult = await provider.InitiateUploadAsync(asset, ct);
    if (initResult.IsFailure) return initResult.Error;

    // 4. Save asset to DB
    await _repository.AddAsync(asset, ct);
    await _unitOfWork.SaveChangesAsync(ct);

    // 5. Map to response
    return MapToResponse(asset.Id, initResult.Value);
}

private InitiateUploadResponse MapToResponse(Guid assetId, UploadInitResult result) =>
    result switch
    {
        S3UploadInitResult s3 => new InitiateUploadResponse(
            assetId,
            StorageProvider.S3,
            new S3UploadData(s3.UploadId, s3.ChunkUrls.Select(...).ToList()),
            null),

        KinescopeUploadInitResult k => new InitiateUploadResponse(
            assetId,
            StorageProvider.Kinescope,
            null,
            new KinescopeUploadData(k.VideoId, k.UploadLink)),

        _ => throw new InvalidOperationException()
    };
```

### GetMediaAssetHandler

```csharp
// FileService.Core/Features/GetMediaAsset/GetMediaAssetHandler.cs
public async Task<Result<GetMediaAssetResponse, Error>> Handle(
    Guid assetId, CancellationToken ct)
{
    var asset = await _repository.GetByIdAsync(assetId, ct);
    if (asset is null) return Errors.NotFound(assetId);

    var accessProvider = _accessProviderResolver.Resolve(asset.StorageReference.Backend);
    var accessInfo = await accessProvider.GetAccessInfoAsync(asset, ct);

    return MapToResponse(asset, accessInfo.Value);
}
```

### KinescopeWebhookEndpoint

```csharp
// FileService.Core/Features/Webhooks/KinescopeWebhookEndpoint.cs
public sealed class KinescopeWebhookEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/kinescope", HandleAsync);

    private async Task<IResult> HandleAsync(
        [FromBody] KinescopeWebhookPayload payload,
        [FromServices] KinescopeWebhookHandler handler,
        CancellationToken ct)
    {
        // Verify signature from header
        // Handle media.update.status event
        await handler.HandleAsync(payload, ct);
        return Results.Ok();
    }
}
```

### AttachExistingVideoEndpoint (Optional)

```csharp
// FileService.Core/Features/AttachKinescopeVideo/AttachKinescopeVideoEndpoint.cs
// Для привязки уже загруженного в Kinescope видео
public sealed class AttachKinescopeVideoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/assets/video/attach", HandleAsync);
}
```

---

## 4. Infrastructure

### EF Core Configuration

```csharp
// FileService.Infrastructure.Postgres/Configurations/MediaAssetConfiguration.cs
builder.Property(x => x.StorageReference)
    .HasColumnName("storage_reference")
    .HasColumnType("jsonb")
    .HasConversion(
        v => JsonSerializer.Serialize(v, JsonOptions),
        v => DeserializeStorageReference(v));

private static StorageReference DeserializeStorageReference(string json)
{
    var doc = JsonDocument.Parse(json);
    var backend = doc.RootElement.GetProperty("Backend").GetString();

    return backend switch
    {
        "S3" => JsonSerializer.Deserialize<S3StorageReference>(json, JsonOptions)!,
        "Kinescope" => JsonSerializer.Deserialize<KinescopeStorageReference>(json, JsonOptions)!,
        _ => throw new InvalidOperationException($"Unknown backend: {backend}")
    };
}
```

### Kinescope Webhook Payload

```csharp
// FileService.Infrastructure.Kinescope/WebhookPayloads/KinescopeWebhookPayload.cs
public record KinescopeWebhookPayload(
    string Type,  // "media.update.status"
    KinescopeWebhookData Data);

public record KinescopeWebhookData(
    string Id,        // Video ID
    string Status,    // "done", "error", etc.
    string? Error);
```

---

## 5. DI Registration

```csharp
// FileService.Core/DependencyInjectionExtensions.cs
services.AddScoped<IUploadProvider, S3UploadProvider>();
services.AddScoped<IUploadProvider, KinescopeUploadProvider>();
services.AddScoped<UploadProviderResolver>();

services.AddScoped<IStorageAccessProvider, S3StorageAccessProvider>();
services.AddScoped<IStorageAccessProvider, KinescopeStorageAccessProvider>();
services.AddScoped<StorageAccessProviderResolver>();
```

---

## Checklist

### Domain ✅

- [x] `StorageBackend` enum
- [x] `StorageReference` abstract + implementations
- [x] `VideoAsset` с `KinescopeStorageReference`
- [x] `PreviewAsset`, `ScreenshotAsset` с `S3StorageReference`

### Contracts

- [ ] `StorageProvider` enum
- [ ] `InitiateUploadResponse` + data records
- [ ] `GetMediaAssetResponse` + data records

### Core/Providers

- [ ] `IUploadProvider` + result types
- [ ] `IStorageAccessProvider` + result types
- [ ] `S3UploadProvider`
- [ ] `KinescopeUploadProvider`
- [ ] `S3StorageAccessProvider`
- [ ] `KinescopeStorageAccessProvider`
- [ ] Resolver classes

### Core/Features

- [ ] `InitiateUploadHandler` refactor
- [ ] `GetMediaAssetHandler`
- [ ] `KinescopeWebhookEndpoint` + handler
- [ ] `AttachKinescopeVideoEndpoint` (optional)

### Infrastructure

- [ ] EF Core JSON configuration for `StorageReference`
- [ ] Webhook payload DTOs
- [ ] Migrations

### Web

- [ ] Endpoint registration
- [ ] DI registration

### Testing

- [ ] Build проекта
- [ ] Интеграционные тесты

