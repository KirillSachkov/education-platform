# Retired data and legacy recovery

The first normal-source deployment is a coordinated retirement boundary. Ordinary
image rollback cannot recover tables and fields removed by its immutable migrations.
The trusted production adapter therefore retains a separate recovery record in
`.github-production/retirement-recovery.json`, owned by root with mode `0600`.

The adapter checks the healthy recorded baseline and all private runtime inputs,
verifies the encrypted runtime backup and an initial database backup, and pulls the
exact target and recovery images. It verifies approved legal/business bytes inside
the exact frontend image as UID 1000 using read-only mounts. Host ancestors remain
root-private; granting UID 1000 access to the host environment directory is unnecessary.

Before applying retirement migrations it:

1. Records the pending target, stops every recorded legacy application, including
   media processing and its producers, and checks that no application or unknown
   database client remains. The known read-only PostgreSQL exporter may remain.
2. Compares stable row fingerprints, creates a fresh post-drain database backup,
   downloads its exact object version, and restores that download to a disposable
   PostgreSQL on `network=none` without published ports. All retained application
   objects, cluster role definitions, role settings and effective memberships must
   compare. Only the known bootstrap-role statements and monitor grant receive
   explicit compatibility handling; SQL errors otherwise stop the operation.
3. Copies the affected author flags, quiz associations, roadmaps, short links and
   progress history to a unique `retained_*` schema in one transaction. Full-row
   symmetric comparisons and subsequent fingerprints guard the copies. The archive
   and source schemas are retained throughout the recovery window.
4. Saves the exact legacy/source identities, normal migration images, archive
   column lists and fingerprints to the private recovery record. An encrypted
   runtime backup includes that record before migrations proceed.
5. Applies the ordinary immutable migration bundles, then checks the retained
   archive and whole-row equality of the drained legacy/FileService transcripts.
   Application health, public/runtime probes and immutable manifest checks still
   gate promotion. Operational role, media and payment acceptance remains required.

A failed stage leaves pending evidence for diagnosis; it never auto-starts old
containers or auto-promotes the target. Failed disposable restore resources remain
identified in `isolated-restore-state.json`; successful drills remove only their own
uniquely labelled container and volume. Application volumes are never restore targets.

## Recorded-previous rollback

Rollback still requires its matching owner command. When crossing back to legacy,
the adapter requires the exact recorded legacy SHA and pulls the retained normal
migration images. It quiesces current writers and verifies a fresh post-drain backup
and isolated restore before changing schemas.

The retained normal Auth, Education and Progress bundles run their existing `Down`
paths to the recorded pre-retirement baselines. Data rehydration restores only
retired structures, rows, fields and flags. It does not replace accounts, orders,
grants, enrollments, bookmarks or the whole database with an earlier snapshot.
Existing core writes are fingerprinted across this step. Changed material content
and deliberately removed associations remain current; unrelated author flags are
preserved. The additive FileService transcript schema remains available.

Before any legacy application starts, the adapter installs a derived copy of its
frozen ingress configuration that rejects POST orders/resume-renewal, starts nginx
without application dependencies and proves both restrictions through HTTPS.
Legacy AccessService receives an explicitly disabled recurring worker; every
case variant in the rendered environment must agree. Historical payment callbacks,
settlement, refunds and grant reads remain available. Frozen original configuration
and financial history are not rewritten to simulate sales closure.

A failure before the archive was created still requires those ingress/worker
restrictions and a verified unchanged retirement baseline before legacy restart.
An unexplained schema, archive, fingerprint, image or runtime mismatch is No-Go.
Keep late-payment reconciliation and signed-webhook idempotency evidence with the
private release acceptance, together with the actual role/access matrix and recovery
timings. Retained schemas, queues and volumes are not disk-cleanup candidates.
