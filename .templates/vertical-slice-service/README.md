# Vertical Slice Microservice Template

For **tiny services** (<10 features, 1-2 aggregates) — collapses the standard
4-project Clean Architecture split (`Domain` + `Core` + `Infrastructure.Postgres` +
`Web`) into **one project** organised by feature folders. Keeps a thin
`{Service}.Contracts` for events / typed clients exposed to other services.

For larger services use `platform-service` instead.

## When to pick VSA vs Clean Architecture

| Signal | Pick VSA | Pick `platform-service` |
|---|---|---|
| Aggregates | 1-2 | 3+ |
| Cross-aggregate workflows | none / simple | multiple |
| Domain logic depth | thin | thick |
| Persistence variants | Postgres only | + S3 / Redis / Typesense |

Rule of thumb: if 80% of code lives under a single `Features/` tree and two
aggregates, choose VSA. ReactionService, LinkService are classic candidates.

## Usage

```bash
dotnet new install .templates/vertical-slice-service

cd backend
dotnet new vertical-slice-service -n ReactionService --port 8011 --schema reactions -o ReactionService

dotnet sln backend.slnx add ReactionService/src/*.csproj ReactionService/tests/*.csproj
```

`-o` is required. `--port` and `--schema` substitute into appsettings,
Dockerfile, Program.cs.

## What's generated

```
backend/{ServiceName}/
├── AGENTS.md
├── Directory.Build.props
├── Dockerfile
├── {ServiceName}.slnx
├── src/
│   ├── {ServiceName}/                       ← single project
│   │   ├── {ServiceName}.csproj
│   │   ├── Program.cs
│   │   ├── Registration.cs
│   │   ├── appsettings.{Development,json}
│   │   ├── Features/Widgets/CreateWidget.cs ← Command + Validator + Endpoint + Handler in one file
│   │   ├── Domain/{ServiceNameErrors,Widgets/{Widget,WidgetName,Events/WidgetEvents}}.cs
│   │   └── Persistence/{DbContext,TransactionManager,Configurations,Repositories}
│   └── {ServiceName}.Contracts/             ← thin
│       ├── Http/CreateWidgetRequest.cs
│       └── IntegrationEvents/               ← add event records here
└── tests/
    └── {ServiceName}.IntegrationTests/
        ├── Infrastructure/                  ← Pattern A factory
        └── Features/Widgets/CreateWidgetTests.cs
```

## Pre-wired

- `PlatformDefaults` from `Shared/PlatformBootstrap` — Serilog + OTel + CORS +
  JWT auth + OpenAPI/Scalar + endpoint discovery.
- EF Core + Npgsql, `TransactionManager` matching `ITransactionManager` contract.
- `IEndpoint` auto-discovery via `MapEndpoints()`.
- `Result<T, Error>` everywhere — no `throw` for business errors.
- IntegrationTestsWebFactory wired for Pattern A (`TestOutboxCollector` from
  `Shared/Wolverine.Testing`).

## Adding a vertical slice

`src/{ServiceName}/Features/{Feature}/{Operation}.cs` — one file containing
Command + Validator + Endpoint + Handler. Delete the feature folder when removing
the feature, but keep committed migrations immutable and add a corrective migration.

## Post-scaffold checklist

1. `dotnet build {ServiceName}.slnx` — 0 errors.
2. Rename `Widget` → your aggregate (5 folders).
3. EF migration:
   ```bash
   dotnet ef migrations add Initial \
     --project backend/{ServiceName}/src/{ServiceName} \
     --startup-project backend/{ServiceName}/src/{ServiceName}
   ```
   (Both flags point at the same project in VSA layout.)
4. Wire infra: docker-compose, nginx, `scripts/ci/github-ci-paths.json`, init-databases.sql, .env.
5. Add a link to `backend/AGENTS.md` only when the generated service is committed.

## When the service outgrows VSA

If you hit 3+ aggregates with cross-aggregate workflows, split out by moving:

1. `Domain/**` → `{ServiceName}.Domain.csproj`
2. `Persistence/**` → `{ServiceName}.Infrastructure.Postgres.csproj`
3. `Features/**` → `{ServiceName}.Core.csproj`
4. `Program.cs` → `{ServiceName}.Web.csproj`

Namespaces don't have to change — only csproj boundaries.
