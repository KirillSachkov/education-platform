# FileService

Owns media assets: S3/MinIO files with presigned upload, image variants, Kinescope videos with
chapters, and the asset registry (entity bindings, drafts, retention) in PostgreSQL schema
`files` (port 8002).

## Context routing

Inspect the affected module under `src/FileService.Core/Features` (`AssetRegistry`, `Files`,
`Videos`), its endpoint and tests. For binding work trace the prepare/confirm/detach revision and
the owning service's handler together. Cross-service backend rules come from
[`../AGENTS.md`](../AGENTS.md).

## Boundary

Single-slot bindings are revisioned: `FileAssetBound` prepares a candidate and only the owner's
`FileAssetBindingConfirmed` makes it active, so stale retries cannot delete the newest asset.
Assets are released when owning entities emit `*HardDeleted` events. Video reconciliation and
binding confirmation do not start AI processing.

Stored SRT export requires `Videos.MANAGE` plus video ownership or platform administrator access.
It reads retained segments for the current provider version without creating jobs. Storage and
the future copy/retirement gate are documented in
[`stored-material-artifacts.md`](../../docs/stored-material-artifacts.md).

## Entrypoint and verification

Entrypoint: `src/FileService.Web/Program.cs`.
Run `dotnet test backend/FileService/tests/FileService.UnitTests` and
`dotnet test backend/FileService/tests/FileService.IntegrationTests`.
