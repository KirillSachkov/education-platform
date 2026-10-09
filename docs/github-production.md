# GitHub production workflow

`.github/workflows/production.yml` runs manually from the source repository's
`main` branch. It uses the `production` environment and one non-cancelling
concurrency group. A merge or image publication never starts production.
The environment must restrict deployment branches to `main`.

The source workflow can exist before operational cutover. Root records actual
SSH, package, backup, deploy and rollback acceptance in the private tracker.
Until that acceptance exists, follow the transition rules in
[release-pipelines.md](agents/release-pipelines.md).

## Select a release

| Operation  | Release               | Inputs                                                                                     |
| ---------- | --------------------- | ------------------------------------------------------------------------------------------ |
| `probe`    | `current`             | No source/build identity                                                                   |
| `deploy`   | `current`             | Approved immutable private current role                                                    |
| `rollback` | `previous`            | Approved immutable private original previous role                                          |
| `rollback` | `recorded-previous`   | Persisted previous release; during failed pending deploy, the last healthy current release |
| `deploy`   | `normal-public-build` | Full public `source_sha` and exact successful main `build_run_id`                          |

Private source identities stay in the environment's `LEGACY_RELEASE_MANIFEST`,
never in dispatch inputs or public logs. Original private roles do not rotate
when `releases/current.env` and `previous.env` change.

A normal release accepts only a completed successful `push` build of the exact
SHA on `main`, in this repository's `ci.yml`. Its unique nonexpired
`image-manifest-<SHA>` artifact must match the archive checksum and all declared source images
canonical image records. Configuration comes from that same source SHA.
There is no latest-build fallback. Persisted manifests and configuration bodies
remain available after GitHub artifact expiry.

## Environment secrets

Configure these only in the private `production` environment:

- `PRODUCTION_SSH_HOST`: independently verified production IPv4 address.
- `PRODUCTION_SSH_KNOWN_HOSTS`: one independently pinned ed25519 host key.
- `PRODUCTION_SSH_PRIVATE_KEY`: dedicated production deployment key.
- `LEGACY_RELEASE_MANIFEST`: schema1 JSON with immutable current/previous roles,
  complete private image manifests, approved PostgreSQL digest, live config
  hashes, `compose_project`, `postgres_volume` and approved `public_url`.
- `LEGACY_GHCR_READ_TOKEN`: dedicated `read:packages` credential.
- `INFISICAL_CLIENT_ID`, `INFISICAL_CLIENT_SECRET`, `INFISICAL_PROJECT_ID`:
  credentials for the independent production secret service.
- `NORMAL_RUNTIME_APPROVAL`: required only for a normal public-source release.

The source repository and forks receive no permission to private legacy
packages. PR jobs receive no production secrets. Detailed SSH and host output
stays private; Actions reports fixed status, operation and service counts.

## Host operation

The host adapter takes an exclusive operation lock. Probe checks configuration,
binary identities, tools, Compose project, database volume and health for the selected
registry (13 legacy services, 9 source services), without registry login, pull, export,
dump or application changes.
Telegram belongs to image and migration inventories but not the blocking
health gate.

Before deploy, validate the current release and reject unresolved transitions.
Export a unique candidate dotenv from Infisical, validate all prior keys and
eight critical secrets, and forbid critical secret rotation. Original private
trials also require unchanged environment values. Compose decodes actual dotenv
values; the adapter never evaluates a dotenv file as shell code.

Before migrations or startup, retain a mode600 `pg_dumpall` gzip archive.
Verify producer exit, gzip integrity and minimum size. Upload that exact archive
under a unique key. Download the same key/version and verify its full checksum,
size, private ACL and anonymous403. A failed guard stops the operation.
The guard never deletes an older archive or applies retention.

Pull exact application, migration and PostgreSQL digests with the dedicated
read-only credential. Verify rollback image availability. Render the approved
Compose configuration with the existing project directory, project name and
PostgreSQL volume. Service env files resolve after the candidate dotenv is
installed. Original trials use captured configuration, including existing
private runtime inputs.

The existing ordered migration runner, media protocol guard and special data
migration/backfill entrypoints remain authoritative. Start the selected release applications
with their dependency gates. Recreate only changed configuration consumers;
file bind mounts otherwise keep the old inode after atomic file replacement.

Require healthy services from the selected registry (13 legacy, 9 source), public page/sitemap/OIDC checks, running
application and PostgreSQL digest identities, and matching configuration.
Then promote release metadata. Preserve a transition receipt and both old
records before changing the current/previous pair. No automatic rollback runs.

## Rollback and recovery

Rollback keeps PostgreSQL, volume, immutable target and media protocol guards.
A failed pending deployment may have unhealthy or mixed application containers.
Recognize its private manifest and accept only known old/pending configuration
hashes. Preserve its evidence before restoring the selected release.

For `recorded-previous`, a recognized failed pending deployment restores the
last healthy current record. Without pending, it restores the persisted
previous record. Restore retained configuration and noncritical environment
values; fresh critical secrets must still match. Run the same backup, pull,
migration dependency and health gates. No schema downgrade occurs.

An interrupted metadata promotion remains blocked by
`.github-production/promotion.pending.json`. Use the saved transition and
pre-promotion records through the incident workflow before another operation.
Never delete this marker to hide an incomplete promotion.

## Private runtime for normal source

The approval must select the exact source SHA, both legal registry hashes,
`LEGAL_DOCUMENTS_DIR`, `LEGAL_PDFS_DIR`, `BUSINESS_DETAILS_FILE`, and every
approved private file hash. Paths live below `/srv/education-platform/`.
Verify actual UID1000 readability, the eight-field business schema, identical
frontend/backend legal versions, and all five current Markdown/PDF pairs.
Empty documents and unfinished placeholders block readiness.

Mount these inputs read-only. After startup, verify current legal pages and
exact approved PDF bytes. Original migration trials ship their original legal
versions; they never ship pending source or new terms.

Only a healthy normal public-source deploy enables the separate release-tag
job. That job receives `contents:write` and no production secrets. It creates
the exact deployed version tag or verifies the existing tag identifies the
same commit. Private migration trials create no release tag.

## Retained legacy configuration

Private current, previous and recorded-previous operations derive their Compose file from the
immutable approved capture. The adapter replaces exactly the archived PostgreSQL registry literal
with its approved GHCR digest and retains both original and derived hashes. Original role manifests
and captured files remain unchanged. Unrecognized retained Compose edits fail before live mutation.

The workflow transports the reviewed isolated restore helper with its other fixed scripts. After
release verification it retains the old helper, installs the reviewed helper with mode0700 and
records its checksum. `RESTORE_POSTGRES_IMAGE` comes from the approved database digest; neither
Compose nor standalone restore falls back to the retired registry.

## Source topology and historical roles

The public build inventory excludes TrainerService, SearchService, TagService and MaterialProcessingService. The frozen private `current` and
`previous` roles retain their original fourteen images, including TrainerService, SearchService, TagService and MaterialProcessingService, for
rollback. `release_model.py` validates each registry against its own complete inventory.
The host adapter selects migrations, startup and health checks from the target registry.
A source merge does not disable an existing production container or remove its data.

A future trainer shutdown requires a private archive, successful isolated restore,
a reviewed disable sequence and matching rollback evidence before any schema retirement.
Committed migrations for these retired services remain at their original paths as history and have no
project or image in the active source build. Source environment validation does not require a
Typesense key; legacy environment validation still requires its original key.

Search and tag schema retirement follows the [retirement procedure](retired-discovery.md).

Material-processing source retirement preserves committed migrations and stored artifacts.
A future schema cleanup requires a verified backup, isolated restore and preserved transcript export.
No source merge runs cleanup or changes the frozen fourteen-image roles.
The FileService copy migration requires stopped legacy writers before execution. Read the
[stored artifact dependencies and future gate](stored-material-artifacts.md) before rollout.
