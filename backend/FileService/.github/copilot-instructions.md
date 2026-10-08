# FileService - AI Coding Agent Instructions

## Architecture Overview

**Clean Architecture with DDD**: Multi-layered .NET 9.0 microservice handling file uploads via S3-compatible storage.

-   **Domain** (`FileService.Domain`): Rich domain models with business rules. Uses inheritance hierarchy for different asset types (`VideoAsset`, `PreviewAsset`) with base class `MediaAsset`. Value objects like `FileName`, `ContentType`, `StorageKey` enforce validation via static factory methods (`Create()`).
-   **Core** (`FileService.Core`): Application logic using **Feature Slices** pattern. Each feature is self-contained: `StartMultipartUpload.cs` contains endpoint definition (`IEndpoint`), handler, and logic. Uses `CSharpFunctionalExtensions` for Railway-Oriented Programming (`Result<T, Error>`).
-   **Infrastructure.Postgres** (`FileService.Infrastructure.Postgres`): EF Core with TPH (Table-Per-Hierarchy) discrimination for `MediaAsset` subtypes. JSON columns for value objects (`MediaData`, `StorageKey`).
-   **Infrastructure.S3** (`FileService.Infrastructure.S3`): AWS S3 SDK wrapper implementing multipart upload workflow with presigned URLs and concurrent request limiting via `SemaphoreSlim`.
-   **Web** (`FileService.Web`): Minimal APIs with auto-registration via `Framework.Endpoints.IEndpoint`. Endpoints mapped in `AppExtensions.Configure()`.
-   **Contracts** (`FileService.Contracts`): DTOs for API communication.

**External Dependencies**: `SachkovTech.*` packages provide shared kernel (`Error`, `Result`), core utilities, and framework helpers (endpoints auto-registration, logging, Swagger).

## Key Patterns & Conventions

### Feature Slices (Vertical Slice Architecture)

Each feature in `FileService.Core/Features/` contains:

```csharp
public sealed class FeatureName : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) { /* route */ }
}

public sealed class FeatureNameHandler
{
    public async Task<Result<TResponse, Error>> Handle(TRequest request, CT ct) { /* logic */ }
}
```

**Registration**: Handlers registered manually in `DependencyInjectionCoreExtensions`. Endpoints auto-discovered via `AddEndpoints(assembly)`.

### Railway-Oriented Programming

All operations return `Result<TSuccess, Error>` or `UnitResult<Error>`. Chain operations with `.Bind()`, `.Map()`, handle failures with `.IsFailure`.

```csharp
Result<FileName, Error> fileNameResult = FileName.Create(request.FileName);
if (fileNameResult.IsFailure)
    return fileNameResult.Error;
```

### Value Objects with Static Factories

Domain primitives like `FileName`, `StorageKey` use private constructors + static `Create()` returning `Result<T, Error>`:

```csharp
public static Result<FileName, Error> Create(string fileName) { /* validation */ }
```

### EF Core Configuration

-   **JSON columns**: Complex value objects stored as JSONB (see `MediaAssetConfiguration.cs`)
-   **TPH inheritance**: `HasDiscriminator<string>("asset_type")` with string values
-   All configurations in `Configurations/` namespace, applied via `ApplyConfigurationsFromAssembly()`

### S3 Multipart Upload Flow

1. **Start**: `StartMultipartUploadAsync()` → `uploadId`
2. **Generate URLs**: `GenerateAllChunksUploadUrlsAsync()` → presigned URLs with expiration
3. **Client uploads** chunks directly to S3
4. **Complete**: `CompleteMultipartUploadAsync()` with ETags

## Development Workflow

### Build & Run

```powershell
dotnet build
dotnet run --project FileService.Web
```

**Configuration**: Uses environment-specific `appsettings.{Environment}.json`. Override via environment variables.

### Testing

Integration tests use **Testcontainers** (PostgreSQL + MinIO):

```powershell
dotnet test
```

Tests in `tests/FileService.IntegrationTests/` override DbContext and S3 client via `IntegrationTestsWebFactory` (inherits `WebApplicationFactory<Program>`). Containers started in `InitializeAsync()`.

### Code Quality

-   **StyleCop** enforced via `Directory.Build.props` with `TreatWarningsAsErrors=true`
-   **Nullable reference types** enabled (`<Nullable>enable</Nullable>`)
-   **Centralized package management**: `Directory.Packages.props` with `ManagePackageVersionsCentrally`

## Critical Implementation Details

### Media Asset Validation

Each asset type (`VideoAsset`, `PreviewAsset`) has:

-   Static `Validate(MediaData)` checking extensions, content types, size limits
-   Constants for `LOCATION` (S3 bucket), `RAW_PREFIX`, allowed extensions
-   `CreateForUpload()` factory calling validation before construction

### Storage Key Structure

`StorageKey` encapsulates: `Location` (bucket), `Prefix` (folder path), `Key` (filename). Normalizes paths, handles slashes. Example: `videos/raw/abc123.mp4`.

### Endpoint Auto-Registration

Framework scans assemblies for `IEndpoint` implementations and calls `MapEndpoint()`. Endpoints grouped under `/api` in `AppExtensions.Configure()`.

### Hybrid Caching

Uses `Microsoft.Extensions.Caching.Hybrid` with Redis backend (configured in `DependencyInjectionCoreExtensions`). Default 5min expiration, 1min local cache.

## Common Tasks

**Add new feature**: Create in `FileService.Core/Features/`, implement `IEndpoint`, register handler in DI. Follow existing feature structure.

**Add new asset type**: Inherit from `MediaAsset`, implement validation, add to `CreateForUpload()` switch, update `MediaAssetConfiguration` discriminator.

**Modify domain model**: Update entity + EF configuration in `Configurations/`. Create migration if schema changes.

**Add integration test**: Inject `IntegrationTestsWebFactory`, use `CreateClient()`. Containers auto-managed per test class.
