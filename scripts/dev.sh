#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.yml"
BACKEND_DIR="$ROOT_DIR/backend"

if [[ ! -f "$ROOT_DIR/.env" ]]; then
  echo "Missing .env file in project root."
  echo "Create it first: cp .env.example .env"
  exit 1
fi

COMPOSE=(docker compose -f "$COMPOSE_FILE")

MIGRATION_SERVICES=(
  comment-service-migrations
  education-service-migrations
  file-service-migrations
  progress-service-migrations
  auth-service-migrations
)

usage() {
  cat <<'EOF'
Usage:
  scripts/dev.sh up-all
  scripts/dev.sh up-infra
  scripts/dev.sh up
  scripts/dev.sh up-obs
  scripts/dev.sh up-fe
  scripts/dev.sh migrate
  scripts/dev.sh gen-fe-env
  scripts/dev.sh down
  scripts/dev.sh backup [name]
  scripts/dev.sh restore <file>
  scripts/dev.sh backups
  scripts/dev.sh logs <service>
  scripts/dev.sh ps
  scripts/dev.sh smoke-notifications

Commands:
  up-all          Start infra + app + frontend profiles (no obs — use up-obs separately)
  up-infra        Start only infra profile
  up              Start infra + app profiles (without frontend)
  up-obs          Start infra + app + obs profiles — единственная команда, поднимающая Grafana stack
  up-fe           Start infra + app + frontend profiles
  migrate         Run all backend migration containers (requires infra)
  gen-fe-env      Generate frontend/.env.local from .env
  down            Stop all profiles and remove orphan containers
  backup          Full pg_dumpall backup (gzipped). Optional name suffix.
  restore         Restore from a backup file (.sql.gz or .sql)
  backups         List available backups
  logs            Tail logs for a service
  ps              Show compose service status
EOF
}

start_profiles() {
  "${COMPOSE[@]}" "$@" up -d --build
}

run_migrations() {
  "${COMPOSE[@]}" --profile infra up -d postgres
  "${COMPOSE[@]}" --profile infra --profile app up --build "${MIGRATION_SERVICES[@]}"
}

BACKUP_DIR="$ROOT_DIR/backups"

create_backup() {
  mkdir -p "$BACKUP_DIR"
  local suffix="${1:-$(date +%Y%m%d_%H%M%S)}"
  local file="$BACKUP_DIR/backup_${suffix}.sql.gz"

  echo "Creating backup..."
  "${COMPOSE[@]}" exec -T postgres pg_dumpall -U postgres --clean --if-exists | gzip > "$file"
  local size
  size=$(du -h "$file" | cut -f1)
  echo "Backup saved: $file ($size)"

  # Keep only the last 10 backups
  local count
  count=$(ls -1 "$BACKUP_DIR"/backup_*.sql.gz 2>/dev/null | wc -l)
  if [[ "$count" -gt 10 ]]; then
    ls -1t "$BACKUP_DIR"/backup_*.sql.gz | tail -n +11 | xargs rm -f
    echo "Cleaned up old backups (keeping last 10)"
  fi
}

restore_backup() {
  local file="$1"

  if [[ ! -f "$file" ]]; then
    # Try relative to backups dir
    file="$BACKUP_DIR/$1"
  fi

  if [[ ! -f "$file" ]]; then
    echo "Backup file not found: $1"
    echo "Available backups:"
    list_backups
    exit 1
  fi

  echo "WARNING: This will overwrite ALL databases with the backup."
  read -rp "Are you sure? (y/N) " confirm
  if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "Restore cancelled."
    exit 0
  fi

  echo "Restoring from $file..."
  if [[ "$file" == *.gz ]]; then
    gunzip -c "$file" | "${COMPOSE[@]}" exec -T postgres psql -U postgres -d postgres
  else
    "${COMPOSE[@]}" exec -T postgres psql -U postgres -d postgres < "$file"
  fi
  echo "Restore complete."
}

list_backups() {
  if [[ ! -d "$BACKUP_DIR" ]] || ! ls "$BACKUP_DIR"/backup_*.sql.gz &>/dev/null; then
    echo "No backups found. Run: scripts/dev.sh backup"
    return
  fi
  echo "Available backups:"
  ls -lh "$BACKUP_DIR"/backup_*.sql.gz | awk '{print "  " $NF " (" $5 ", " $6 " " $7 " " $8 ")"}'
}

generate_frontend_env() {
  local env_file="$ROOT_DIR/.env"
  local out="$ROOT_DIR/frontend/.env.local"

  local keys=(
    AUTH_SECRET
    AUTH_OIDC_ID
    AUTH_OIDC_SECRET
    AUTH_OIDC_ISSUER
    NEXT_PUBLIC_APP_URL
    NEXT_PUBLIC_AUTH_ORIGIN
    NEXT_PUBLIC_API_URL
    NEXT_PUBLIC_PRIMARY_AUTHOR_SLUG
    NEXT_PUBLIC_CONSULTATION_LINK
    NEXT_PUBLIC_VAPID_PUBLIC_KEY
  )

  {
    echo "# Auto-generated from .env by: scripts/dev.sh gen-fe-env"
    echo "# Do not edit manually — re-run the command to refresh."
    echo ""

    for key in "${keys[@]}"; do
      grep -E "^${key}=" "$env_file" || echo "# WARNING: ${key} not found in .env"
    done

    echo ""
    echo "# NextAuth base URL — access frontend through nginx, not :3000 directly"
    local app_url
    app_url=$(grep -E '^NEXT_PUBLIC_APP_URL=' "$env_file" | cut -d= -f2-)
    echo "AUTH_URL=${app_url:-http://localhost}"

    echo ""
    echo "# OpenTelemetry"
    local otel_endpoint
    otel_endpoint=$(grep -E '^FRONTEND_OTEL_EXPORTER_OTLP_ENDPOINT=' "$env_file" | cut -d= -f2-)
    if [[ -n "$otel_endpoint" ]]; then
      echo "OTEL_EXPORTER_OTLP_ENDPOINT=${otel_endpoint}"
    fi
  } > "$out"

  echo "Generated $out"
}

case "${1:-}" in
  up-all)
    start_profiles --profile infra --profile app --profile frontend
    ;;
  up-infra)
    start_profiles --profile infra
    ;;
  up)
    start_profiles --profile infra --profile app
    ;;
  up-obs)
    # Единственный путь, по которому бэкенды реально пушат telemetry: только здесь
    # экспортим endpoint, чтобы compose interpolated его в service environment.
    export OTEL_EXPORTER_OTLP_ENDPOINT="http://otel-collector:4317"
    start_profiles --profile infra --profile app --profile obs
    ;;
  up-fe)
    start_profiles --profile infra --profile app --profile frontend
    ;;
  migrate)
    run_migrations
    ;;
  gen-fe-env)
    generate_frontend_env
    ;;
  down)
    "${COMPOSE[@]}" --profile infra --profile app --profile frontend --profile obs down --remove-orphans
    ;;
  backup)
    create_backup "${2:-}"
    ;;
  restore)
    if [[ -z "${2:-}" ]]; then
      echo "Usage: scripts/dev.sh restore <backup-file>"
      list_backups
      exit 1
    fi
    restore_backup "$2"
    ;;
  backups)
    list_backups
    ;;
  logs)
    if [[ -z "${2:-}" ]]; then
      echo "Service name is required for logs command"
      usage
      exit 1
    fi
    "${COMPOSE[@]}" logs -f "$2"
    ;;
  ps)
    "${COMPOSE[@]}" ps
    ;;
  smoke-notifications)
    # E2E smoke test для NotificationService. Требует поднятый dev stack.
    BASE_URL="${BASE_URL:-http://localhost}" "$ROOT_DIR/scripts/smoke-notifications.sh"
    ;;
  *)
    usage
    exit 1
    ;;
esac
