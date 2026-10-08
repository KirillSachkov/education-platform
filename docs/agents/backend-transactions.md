---
paths: ["backend/**/*.cs"]
---

# Backend persistence and transactions

Code comments cite these rules by number.

1. Command handlers persist through the service's `ITransactionManager`. It flushes the Wolverine
   outbox atomically with domain state, dispatches domain events, and maps concurrency and
   cancellation failures to `UnitResult<Error>`. A plain `DbContext.SaveChangesAsync` drops
   buffered envelopes; a buffered envelope without a successful flush is not delivery evidence.
2. Repositories expose query and mutation primitives (`AddAsync`, `GetByAsync`, `GetManyByAsync`,
   purpose-built projections), never their own `SaveChangesAsync` boundary.
3. Load the complete aggregate state needed to enforce invariants. When a child collection
   participates in the decision, load it with an explicit `.Include()` in the repository instead of
   relying on `AutoInclude`.
4. Aggregate roots saved through `DbSet.AddAsync` get `Guid.CreateVersion7()` in their domain
   factory, so domain events carry the real ID. A child entity added through a navigation
   collection (`parent.Children.Add(child)`) keeps `Id = Guid.Empty` and is configured with
   `HasValueGenerator<TimeOrderedGuidValueGenerator>().ValueGeneratedOnAdd()` (`Shared/Database`):
   EF treats a non-empty key discovered in a collection as Modified and the insert fails with
   `DbUpdateConcurrencyException`.
5. Use an explicit transaction only when one use case must atomically coordinate multiple saves or
   aggregates. Keep EF state manipulation in persistence code: a handler loads, invokes domain
   behavior, publishes, and commits; it does not repair ChangeTracker state ad hoc.

Queries do not save. One-off recovery tools may use a lower-level persistence API only when their
scope and operational authorization explicitly allow it.
