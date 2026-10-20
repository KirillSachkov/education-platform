# Protected production environment

The normal runtime has no Infisical container. One root-owned `0600` file,
`/srv/education-platform/runtime.env`, supplies deployment inputs. Its parent
directory is root-owned `0700`. `/opt/education-platform/.env` is the applied copy
and must not be edited independently. The adapter reads dotenv without shell
evaluation, rejects symlinks, incorrect ownership, permissive modes, duplicate
keys, missing critical values and accidental critical key rotation. The existing
release, image, PostgreSQL volume and rollback guards remain in force.

## Authorized cutover

Preparation never reads real secrets or changes production. During the separately
authorized cutover:

1. Install `age` and verify the offsite bucket supports object versioning. Keep
   existing object-storage credentials unchanged. The bucket credential must allow
   private put, versioned head and versioned get operations.
2. Preserve the current resolved environment privately. Copy it byte-for-byte to
   `runtime.env` under the protected directory, retaining all legacy keys while
   a historical rollback is supported. Do not print values or pass them as command
   arguments. Validate the candidate against the applied file and freeze the
   accepted release records before deployment.
3. Generate an age identity on the owner's trusted off-host machine, retain it in
   the owner's private recovery storage and verify a recovery copy. Install only
   its public `age1...` recipient as
   `/srv/education-platform/backup-age-recipient`, root-owned and not writable by
   group/others. The decryption identity never belongs on production or in CI.
4. Run the synthetic restore checks and a private real encrypted-backup/restore
   drill before retiring the secrets service. Verify every restored manifest
   checksum and required environment/release record without logging contents.
5. Deploy the reviewed source through the normal manual release workflow. Replace
   the previous daily shell-sourcing cron with
   `python3 /opt/education-platform/scripts/ops/production/daily_backup.py` at
   03:00 UTC, under root, with the existing private log destination. Ensure exactly
   one daily schedule is enabled. It reads the applied dotenv without shell
   evaluation and backs up the canonical input separately.
6. After acceptance, remove only the inventoried Infisical container and its
   obsolete workflow credentials. Keep private legacy snapshots until the agreed
   recovery window ends. Removing a Compose declaration alone does not stop a
   running old container or free its storage.

## Encrypted recovery copy

`backup_runtime.py` creates a manifest-hashed tar.gz in memory containing the
canonical and applied environment and accepted current/previous/pending release
metadata/configuration. Existing interrupted-promotion evidence is included.
Only age ciphertext is written to a temporary file or uploaded. Each private S3
object has a unique key under `runtime/`, a required version ID and ciphertext
checksum metadata. The exact version is downloaded and its entire checksum
compared before an atomic private receipt reports success. Failure blocks a
deployment before the database backup, migrations or live-file replacement;
daily failure prevents refreshing the verified database receipt.

Daily runs preserve current recovery inputs after manual canonical edits. Runtime
archive versions have no automatic expiration in this change: keep the identity
and object-storage access outside the server. Database dumps, legal/business
assets, TLS certificates and other host files have their own existing recovery
procedures; this archive does not replace them.

## Recovery without a secrets service

On an isolated trusted machine, download the receipt's exact bucket/key/version,
verify the ciphertext SHA-256, then decrypt with the off-host age identity. Inspect
the archive member list before extraction into a new private directory; accept
only regular relative files from the fixed manifest. Check every member's size
and SHA-256 against `manifest.json`. Never extract directly over a live runtime.

Restore the canonical environment to the protected runtime directory and the
applied environment/release records to their original paths, all root-owned
`0600`. Preserve a failed deployment's evidence. Restore the approved images and
configuration from those release records, use the existing isolated database
restore procedure, then verify health and application data before traffic cutover.
Historical rollback may recreate its captured legacy stack, but the host adapter
and restore path do not log in to or export from Infisical.
