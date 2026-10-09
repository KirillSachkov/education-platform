# Retired discovery features

SearchService, TagService and Typesense have no active source build, Compose service, proxy route
or consumer. Their committed migrations remain immutable history. Source release inventories
exclude their images; archived legacy release inventories retain the original images and
configuration for recovery.

Course search uses the existing EducationContentService material feed. It trims the query and
matches a literal, case-insensitive substring of the published material title in the selected
course. Wildcards have no special meaning. Existing kind filters and cursor pagination still
apply. Drafts and materials from other courses are excluded. Locked published cards retain only
the metadata permitted by the material lifecycle; their preview is redacted. Missing access
decisions are treated as denial. Searching never grants access to the material body.

Roadmaps, short links, material notes and certificates have no active handlers, repositories
or models. Separate corrective ECS and Progress migrations remove only their tables. Auth removes
only the retired Roadmaps flag from author contracts and stored JSON; its earlier Leaderboard
retirement remains a separate migration. Other author values and identity data remain intact.

Bookmarks and course collections remain active. Course search does not filter collections or
change collection progress. Legacy material detail routes remain available under their existing
access checks, including links for purchased content. The global discovery showcase is retired.

## Future retirement operation

This source change does not authorize a production operation. Schema removal needs a separately
approved release and recovery plan.

1. Inventory live legacy services, schema sizes and external consumers in private evidence.
   Verify that no active consumer depends on SearchService, TagService or Typesense.
2. Capture a private full database backup and the matching legacy image/configuration inventory.
   Verify checksums and restore the exact backup in isolation before proceeding.
3. Verify counts and representative access/progress flows for purchased courses, materials,
   homework, bookmarks and course collections in the restored database.
4. Review a forward and rollback sequence. Apply corrective ECS/Progress/Auth migrations only during
   the approved release; retain their old migration history. These migrations must not drop
   bookmarks, collections, material content, grants, retained progress or other author flags.
5. Stop only the retired discovery consumers and Typesense during that approved operation.
   Verify the source inventory, course search, bookmark round-trip and collection progress.
6. Review schema deletion separately after the recovery evidence and consumer checks pass.
   `search` and `tags` are retired schemas; inspect catalog ownership and cross-schema dependencies
   before preparing a one-off deletion. Do not run blind `CASCADE` against retained content.
7. Keep backup artifacts and immutable legacy roles. If restoration is needed, stop writes,
   preserve the failed state, restore the exact verified backup and its matching legacy
   configuration, then verify retained data and access again. Merely reverting the source branch
   cannot recover rows removed by a corrective migration.

Local integration fixtures exercise fresh and upgraded service databases. They are not a
production backup or a production restore rehearsal.
