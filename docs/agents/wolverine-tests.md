---
paths: ["backend/**/*.cs"]
---

# Wolverine, outbox, and tests

## Production contract

`IOutboxService.PublishAsync` buffers a message. A command that expects delivery must subsequently
save or commit through `ITransactionManager`; returning before that flush may drop the envelope.
Consumers are idempotent because durable delivery can repeat.

## Test seams

Use the pattern already owned by the affected integration factory:

- Collector pattern: disable external transports and Wolverine persistence, replace the service
  outbox/transaction manager with `backend/Shared/Wolverine.Testing` adapters, and assert collected
  messages. Clear the collector with database reset.
- Tracked-session pattern: when an existing suite uses `Host.TrackActivity()`, retain its Wolverine
  persistence setup and wait for the tracked message/handler result. Coordinate Respawn with active
  durability work; do not copy this pattern into a collector-based suite.

Every factory disables real external transports and removes broker health checks it cannot satisfy.
When an endpoint adds a rate-limit policy, register a no-limit test equivalent or the application
can fail at runtime before the assertion.

Feature suites cover handler behavior and the service's publish/consume seam. External Wolverine
transports are stubbed in tests; a real-broker round-trip is out of scope. Routing keys and wire
names follow `MESSAGING_CONVENTIONS.md`; do not add a RabbitMQ topology test to every service.
