# Platform Microservice Template

One-shot scaffold for a new `.NET 10` microservice matching the platform's
Clean Architecture + DDD + `Result<T, Error>` stack.

The template is intentionally **minimal** — no Wolverine outbox, no RabbitMQ
wiring, no service-to-service HTTP client. Add those pieces explicitly when
the service actually needs them (CommentService is the canonical reference for
Wolverine + outbox; AuthService.Contracts shows the typed HTTP client pattern).

## Usage

```bash
# From the repo root — install once per machine
dotnet new install .templates/platform-service

# Scaffold a new service into backend/{TrainerService}/
cd backend
dotnet new platform-service -n OrdersService --port 8010 --schema orders -o OrdersService

# Register projects with the solution
dotnet sln backend.slnx add OrdersService/src/*.csproj OrdersService/tests/*.csproj
```

`-o` controls the output directory. Without it, files land in the current
directory and overwrite root-level `Directory.Build.props` / `CLAUDE.md` —
always pass `-o {TrainerService}`.

`--port` and `--schema` are substituted everywhere (`appsettings.json`,
`Dockerfile`, `Program.cs`, integration test setup).

## What's generated

```
backend/{TrainerService}/
├── CLAUDE.md                                           # service-specific docs stub
├── Directory.Build.props                               # imports root analyzers
├── Dockerfile                                          # multi-stage runtime image
├── {TrainerService}.slnx                                  # local solution
├── src/
│   ├── {TrainerService}.Domain/                          # Widget aggregate + value object + events
│   ├── {TrainerService}.Contracts/                       # request DTOs (no HTTP client by default)
│   ├── {TrainerService}.Core/                            # CreateWidget vertical slice + Registration
│   ├── {TrainerService}.Infrastructure.Postgres/         # DbContext, TransactionManager, repository
│   └── {TrainerService}.Web/                             # 15-line Program.cs with PlatformDefaults
└── tests/
    └── {TrainerService}.IntegrationTests/                # Testcontainers + Respawn baseline
```

- **Domain** — `Widget : AggregateRoot` with private ctor + static `Create`,
  `WidgetName` value object, `WidgetCreatedEvent` / `WidgetRenamedEvent`
  domain events.
- **Core** — `IWidgetsRepository` (Expression-based filters), `CreateWidget`
  vertical slice (command + validator + endpoint + handler in one file).
- **Infrastructure.Postgres** — `{TrainerService}DbContext` with default schema,
  `TransactionManager` matching `SachkovTech.Core.Database.ITransactionManager`
  contract (BeginTransaction / Commit / SaveChanges / GetDbConnection),
  domain-event dispatch loop (cascades supported, bounded by request `CancellationToken`).
- **Web** — `builder.AddPlatformDefaults("{TrainerService}")` → Serilog + OTel +
  JWT + CORS + OpenAPI/Scalar + endpoint discovery wired in one call.
- **Tests** — `IntegrationTestsWebFactory` with Testcontainers PostgreSQL +
  Respawn, one sample test per use case.

## Post-scaffold checklist

1. `dotnet build backend.slnx` — verify zero errors before changing anything.
2. **Replace `Widget`** with your real aggregate. Touch:
   - `src/{TrainerService}.Domain/Widgets/` — rename to your aggregate folder.
   - `src/{TrainerService}.Core/Features/Widgets/` — rename feature folder.
   - `src/{TrainerService}.Core/Database/IWidgetsRepository.cs` — rename interface.
   - `src/{TrainerService}.Infrastructure.Postgres/Repositories/`,
     `Configurations/` — rename files.
   - `src/{TrainerService}.Contracts/CreateWidgetRequest.cs` — your request DTO.
   - `tests/{TrainerService}.IntegrationTests/Features/Widgets/` — rename folder.
3. Create the initial EF migration:
   ```bash
   dotnet ef migrations add Initial \
     --project backend/{TrainerService}/src/{TrainerService}.Infrastructure.Postgres \
     --startup-project backend/{TrainerService}/src/{TrainerService}.Web
   ```
4. **Wire infrastructure** (each step is a separate concern — only do what you need):
   - `docker-compose.yml` + `docker-compose.prod.yml` — service entry +
     migration sidecar (copy from a neighbour like CommentService).
   - `nginx.conf` + `nginx.prod.conf` — `location /api/{path}/ { ... }` upstream.
   - `scripts/ci/github-ci-paths.json` — register the service image and build context.
   - `docker/postgres/init-databases.sql` — add `CREATE SCHEMA {schema}`.
   - `.env.example` — connection string entry.
5. Add the service row to root `CLAUDE.md` "Services" table.

## Adding Wolverine + RabbitMQ outbox

If the new service needs to publish or consume integration events:

1. Add NuGet refs to `{TrainerService}.Core.csproj` and
   `{TrainerService}.Infrastructure.Postgres.csproj`:
   ```xml
   <PackageReference Include="JasperFx" />
   <PackageReference Include="WolverineFx" />
   <PackageReference Include="WolverineFx.EntityFrameworkCore" />
   <PackageReference Include="WolverineFx.Postgresql" />
   <PackageReference Include="WolverineFx.RabbitMQ" />
   ```
2. Add `ProjectReference` on `Shared/Messaging/RabbitMqMessaging` from Core.
3. Copy the canonical `WolverineConfiguration` from
   `backend/CommentService/src/CommentService.Core/Messaging/WolverineConfiguration.cs`.
4. Update `Program.cs`:
   ```csharp
   builder.AddPlatformDefaults("{TrainerService}");
   builder.Services.AddCore(builder.Configuration).AddInfrastructurePostgres(builder.Configuration);
   builder.AddWolverine();        // ← add this
   ```
5. Replace `TransactionManager.cs` with the Wolverine-aware version
   (CommentService's `TransactionManager.cs` — uses `IDbContextOutbox<TDbContext>`
   for atomic save + outbox flush). Add `IOutboxService` if other code needs to
   publish events outside use-case handlers.
6. Map Wolverine envelope tables in `{TrainerService}DbContext`:
   ```csharp
   modelBuilder.MapWolverineEnvelopeStorage("{schema}");
   ```
7. Update `IntegrationTestsWebFactory.cs`:
   - `services.DisableAllExternalWolverineTransports();`
   - `await WolverineSchemaHelper.CreateTablesAsync(dbContext);` after migrate.
   - Add `ConnectionStrings:RabbitMq = "amqp://localhost:5672"` setting.

See `docs/agents/wolverine-tests.md` for the L1/L2 testing model and the
flush-or-lose-events checklist.

## Adding a typed HTTP client

If other services need to call this one over HTTP, add a `Contracts/HttpClient/`
folder with `I{TrainerService}Client` + implementation extending
`Core.HttpCommunication.BaseHttpClient` from `SachkovTech.Core`. See
`AuthService.Contracts` or `EducationContentService.Contracts` for the
canonical pattern. Don't forget to register the client + auth handler in the
consumer's DI.
