# Disk retention before a smaller host

Preparation does not delete production files. First accept the simplified release,
verify offsite database and encrypted runtime recovery, exercise rollback, and
retire only the reviewed obsolete containers. Their volumes have a separate
explicit retirement inventory. Never run volume prune or broad system prune.

## Release image retention

`scripts/ops/production/cleanup_disk.py` defaults to a read-only plan. It requires
root-private current and previous release manifests and preserves their exact
application and PostgreSQL images, optional pending images, explicitly independent
images and every image used by any running or stopped container. Missing recovery
records/images or interrupted promotion block cleanup.

Only images at least seven days old, with every tag/digest repository in the
accepted allowlist, can be selected. Normal and migrated legacy GHCR application
repositories are known from the release model. Untagged, mixed-owner and unknown
images are preserved. Add older retired-registry repositories only from a private
fresh host inventory; never guess a prefix or publish private host addresses.

Prepare root-owned mode `0600` `/etc/education-platform/cleanup.json`:

```json
{
  "legacy_repositories": ["registry.example/retired/platform/auth-service"],
  "independent_images": ["registry.example/retained-tool@sha256:replace-with-reviewed-digest"],
  "backup_directory": "/opt/education-platform/backups",
  "local_archives": []
}
```

Replace illustrative values with reviewed actual repositories and immutable
digests, or use empty lists where no extra inputs are needed. Normal release
delivery installs this tool and its module dependencies with exact source hashes.
Keep private plan output in a root-only file:

```bash
umask 077
python3 /opt/education-platform/scripts/ops/production/cleanup_disk.py \
  --config /etc/education-platform/cleanup.json > /root/cleanup-plan.json
```

Review the complete candidate inventory and protected image IDs. The separately
authorized destructive command adds `--apply --plan-sha256 <accepted-plan-sha256>`.
It reacquires the same exclusive lock as deployment and recomputes the inventory;
any mismatch refuses deletion. Docker image removal never uses force. No container,
volume, builder cache or unrelated repository is deleted. Keep the private result
and measure filesystem use afterward: image size sums overlap shared layers and
are not an estimate of bytes reclaimed.

Run this reviewed retention procedure after an authorized future manual release
and its recovery window. With development stopped, there is no background image
build/pull schedule. Do not add an unattended destructive job that accepts its own
new plan. Preexisting builder caches and runner files remain preserved until a
separate ownership check authorizes their exact paths.

## Local backups

The daily backup removes its new local dump only after verified private upload.
It retains failed dumps as recovery evidence; the five-minute monitor detects a
stale receipt or low disk space. Investigate failures rather than deleting their
only copy.

For an old local archive, add one explicit `local_archives` receipt containing
`path`, `bucket`, `key`, `version_id`, `sha256` and `size_bytes`. Its basename must
match the object key and it must be an ordinary root-private file directly inside
the approved backup directory, at least seven days old. Missing version IDs,
symlinks or mismatches refuse cleanup. Before deleting anything, apply downloads
every selected exact object version to a private temporary directory and compares
the full bytes/checksum. The local file is checked again before unlinking.
Unlisted archives remain preserved. Runtime archives are encrypted separately and
have no automatic version expiry in this preparation.

## Host and container logs

Compose already rotates container JSON logs at 10 MB with three files.
After the authorized cutover, install `docker/logrotate/education-platform` as
root-owned `0644` `/etc/logrotate.d/education-platform`, validate it with
`logrotate --debug`, and check the existing daily `logrotate.timer`. The profile
keeps three compressed rotations and requests rotation at the next check when
nginx logs exceed 20 MB or the backup log exceeds 10 MB. nginx receives USR1 to
reopen logs; verify its name, project, master permissions and bind path first.
For a different installation path adapt the profile before installing it.
These are periodic size thresholds, not hard per-second filesystem quotas.

Install `docker/journald/education-platform.conf` under
`/etc/systemd/journald.conf.d/` only after reviewing this host-wide policy. It
limits persistent journals to 256 MB, runtime journals to 64 MB and age to seven
days. Restart journald during the authorized maintenance, inspect its status,
then explicitly vacuum historical journals to the accepted policy. Do not delete
active log files by hand. Preserve any incident evidence separately first.

## Sizing evidence

Record `df`, container count and per-container memory before/after retirement,
actual image/storage footprint after retention, database size and daily backup
growth. Observe at least one daily backup and representative application peaks.
Choose the new RAM/disk from that retained footprint plus growth and recovery
headroom; a screenshot of mean CPU/RAM does not establish a smaller safe target.
The server purchase/move remains a separate operation after these measurements.
