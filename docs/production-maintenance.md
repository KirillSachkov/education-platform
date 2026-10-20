# Small production runtime

The normal runtime keeps the application, PostgreSQL, Redis, RabbitMQ and nginx.
Grafana, Prometheus, Loki, Tempo, Alloy, OTEL Collector and the PostgreSQL/Redis
exporters are absent from production Compose. Optional local debugging still uses
`scripts/dev.sh up-obs` and the development configurations.

Production backend containers explicitly set `OTEL_SDK_DISABLED=true` and clear
the OTLP endpoint. OpenTelemetry .NET 1.15+ supports this switch for all signals;
the application uses 1.16. Serilog continues writing error and application logs to
stdout. Compose rotates Docker logs at 10 MB with three files. nginx host logs
need the host retention procedure before the migration.

## Lightweight checks

`scripts/ops/production/check_health.py` checks the accepted release's application
containers, PostgreSQL, RabbitMQ, Redis and nginx, the public HTTPS page, free disk
space (at least 15%) and the latest verified daily backup receipt (at most 30 hours
old). Container checks validate the Compose project and service identity. The
read-only check does not restart containers or modify application data.

Prepare `/etc/education-platform/monitor.json`, owned by root with mode `0600`:

```json
{
  "root": "/opt/education-platform",
  "project": "education-platform",
  "public_url": "https://platform.example/",
  "backup_receipt": "/var/lib/education-platform/last-backup.json",
  "alert_webhook_url": "https://alerts.example/approved-private-endpoint"
}
```

The webhook must accept `{"text":"..."}`. Configure an owner-selected alert
destination and verify actual delivery during the authorized production cutover.
No real alert is sent by preparation or unit tests. The command without `--notify`
prints a small PASS/FAIL report; the explicit flag enables failure notifications.
It never sends inspect output, environment values or private URLs. A failed
notification remains a failed check. Persistent failures notify every five minutes.

After the first successful normal deployment and daily backup, the authorized
operator installs the two unit files from `docker/systemd/` into
`/etc/systemd/system/`, runs `systemctl daemon-reload`, checks the service once,
and enables `education-platform-health.timer`. These host paths assume the
standard installation directory. For another directory adapt the unit before
installation. Review status with `systemctl status education-platform-health.timer`
and `journalctl -u education-platform-health.service`.

## Backup evidence

The daily `backup-s3.sh` validates gzip, uploads a private object with SHA-256
metadata, and checks the remote size and checksum metadata before removing the
local dump. Only then it atomically writes the root-private
`/var/lib/education-platform/last-backup.json` receipt. A read-back mismatch keeps
the local dump and the preceding receipt. This verifies upload/read-back metadata;
it does not replace an isolated restore drill.

## Production transition and recovery

Preparation changes source only. During the separately authorized release:

1. Capture the actual current/previous release configuration and images. Validate
   the fresh offsite database backup and perform the restore drill.
2. Deploy the accepted simplified application source and verify application health,
   learning/access/assignments, retained subtitles and the public page.
3. Remove only retired monitoring containers after confirming their project and
   mounts. The normal Compose file no longer declares them; never use volume prune.
4. Run a daily backup to establish its verified receipt; install and exercise the
   lightweight timer and owner-selected alert route.
5. After acceptance and the agreed recovery window, delete only the named retired
   monitoring volumes from the reviewed inventory. They are not application data.
   Record the resulting RAM and disk footprint before selecting a smaller server.

Old containers or volumes can remain until that explicit retirement step. Merely
removing them from Compose does not prove they stopped or that disk was freed.
Private legacy manifests/configuration remain frozen for their historical release
path; normal monitoring changes do not rewrite those recovery inputs.
