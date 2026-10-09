# Backend

.NET 10 services use Clean Architecture, vertical slices, EF Core/PostgreSQL, Dapper reads, and
the shared Result/Error contract. Each service owns its process, schema, domain model, and use
cases; shared primitives live under `Shared/`.

## Before changes

- Read the target service's `AGENTS.md`, then inspect the changed domain, handler, endpoint,
  persistence, and test code. Code and executable tests are the authority for service detail.
- Path-scoped rules under [`../docs/agents/`](../docs/agents/) are authoritative for
  transactions, migrations, Wolverine, integration tests, and quality gates.

## Backend invariants

- A use case keeps its command/query, validator, endpoint, and handler in one
  `Features/{Feature}/UseCases/{Action}.cs` file; endpoints and handlers are auto-registered.
- Services do not reference another service's Domain/Core/Infrastructure assemblies. Cross-service
  access goes through a `.Contracts` HTTP client or an integration event. A cross-schema read is
  allowed only through a versioned read contract
  ([ADR-001](../docs/architecture-decisions/ADR-001-versioned-db-read-contracts.md)).
- Persist through `ITransactionManager`; do not hide `SaveChanges` inside repositories.
- Business failures return Result/Error. Error `message` is user-facing Russian; error `code`
  (`domain.entity.condition`) is a stable API contract and is never renamed.
- Enums stored or serialized as strings use `UPPER_SNAKE_CASE` members, `HasConversion<string>()`,
  and no `HasDefaultValue`; the aggregate constructor sets the value.
- Production identifiers use `Guid.CreateVersion7()`. API URLs include a trailing slash.
- Existing migrations are immutable. Read [`../docs/agents/migrations.md`](../docs/agents/migrations.md)
  before touching `Migrations/` or `DataMigrations/`.
- Messaging changes update the consumer's `AGENTS.md` when it states the flow and the canonical
  [`MESSAGING_CONVENTIONS.md`](Shared/Messaging/RabbitMqMessaging/MESSAGING_CONVENTIONS.md).
- Resource-level content access goes through `Shared/ContentAccess` (`IEntitlementChecker`,
  fail-closed on Redis errors); tests use `FakeEntitlementChecker`.

## Service entrypoints

- [AccessService](AccessService/AGENTS.md)
- [AssignmentReviewService](AssignmentReviewService/AGENTS.md)
- [AuthService](AuthService/AGENTS.md)
- [CommentService](CommentService/AGENTS.md)
- [EducationContentService](EducationContentService/AGENTS.md)
- [FileService](FileService/AGENTS.md)
- [NotificationService](NotificationService/AGENTS.md)
- [ProgressService](ProgressService/AGENTS.md)
- [TelegramBotService](TelegramBotService/AGENTS.md)
- [Shared AI](Shared/AI/AGENTS.md)

`SearchService`, `TagService`, and `MaterialProcessingService` are scheduled for removal and have
no instruction scope; inspect their code directly and do not extend them.

## Verification

Build with `dotnet build backend/backend.slnx`. Run the affected service's focused tests; use
[`../docs/agents/testing-profile.md`](../docs/agents/testing-profile.md) to select heavier evidence.
