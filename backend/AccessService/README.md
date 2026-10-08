# Platform Microservice Template

One-shot scaffold for a new `.NET 10` microservice matching the platform's
Clean Architecture + DDD + Result<T, Error> + Wolverine outbox stack.

## Usage

```bash
# From the repo root — install once per machine
dotnet new install .templates/platform-service

# Scaffold a new service at the correct path
cd backend
dotnet new platform-service -n OrdersService --port 8010 --schema orders
# → creates backend/OrdersService/src/*, tests/*, Dockerfile, CLAUDE.md, slnx

# Register it with the solution
dotnet sln ../backend.slnx add OrdersService/src/*.csproj OrdersService/tests/*.csproj
```

Replace `AccessService` with your PascalCase name (`OrdersService`). `--port`
and `--schema` are set per-service. Everything else (ProjectReferences,
Dockerfile COPY paths, Program.cs bootstrap, integration test base) is
wired correctly for the monorepo layout from day one.

## What's inside

- **Domain layer** — `Widget` aggregate with a `WidgetName` value object,
  `Create` factory returning `Result<Widget, Error>`, domain event sample.
- **Core layer** — `IWidgetsRepository` contract, `CreateWidgetHandler`
  (command + validator + endpoint + handler in one vertical slice file),
  integration with `TransactionManager`.
- **Infrastructure.Postgres layer** — `AccessServiceDbContext`, EF Core
  configuration, Wolverine outbox wiring, Dapper-ready connection string
  search path.
- **Contracts layer** — typed HTTP client pattern (consumer services can
  call `IAccessServiceClient.GetWidget(...)`).
- **Web layer** — 15-line `Program.cs` using `builder.AddPlatformDefaults()`
  and `app.UsePlatformDefaults()`. Serilog + OTEL + JWT + CORS + health +
  Scalar UI all wired by default.
- **Tests** — `AccessServiceTestsBase`, `IntegrationTestsWebFactory`
  (Testcontainers PostgreSQL + Respawn), one sample test per use case.
- **CI** — no changes needed; `.gitlab-ci.yml` template glob picks up the
  new service automatically once you add a `build-{service}` job.

## After scaffolding

1. Add to `backend/backend.slnx`.
2. Add `build-ordersservice` job to `.gitlab-ci.yml` (copy neighbour).
3. Add service entry + migration sidecar to `docker-compose.yml`.
4. Add `location /api/orders/ { ... }` upstream to `nginx.conf`.
5. Create initial EF migration: `dotnet ef migrations add Initial --project OrdersService/src/*.Infrastructure.Postgres --startup-project OrdersService/src/*.Web`.
6. Customize `CLAUDE.md` with domain-specific rules.

The generated service builds + tests green on the first try.
