#!/usr/bin/env bash
set -euo pipefail

umask 077

ENV_FILE="${GITLAB_BACKUP_ALERT_ENV_FILE:-/etc/gitlab-backup/alert.env}"
FAILED_UNIT="${1:-}"

fail() {
  printf 'gitlab-backup-alert: %s\n' "$*" >&2
  exit 1
}

[[ "$FAILED_UNIT" =~ ^[A-Za-z0-9@_.:-]+$ ]] || fail "invalid failed unit name"
[[ -f "$ENV_FILE" ]] || fail "alert environment file is missing"

env_mode="$(stat -c '%a' "$ENV_FILE")"
env_owner="$(stat -c '%u:%g' "$ENV_FILE")"
[[ "$env_mode" == "600" && "$env_owner" == "0:0" ]] \
  || fail "alert environment file must be root:root mode 600"

: "${TELEGRAM_BOT_TOKEN:?TELEGRAM_BOT_TOKEN is required}"
: "${TELEGRAM_CHAT_ID:?TELEGRAM_CHAT_ID is required}"

[[ "$TELEGRAM_BOT_TOKEN" =~ ^[0-9]+:[A-Za-z0-9_-]+$ ]] \
  || fail "TELEGRAM_BOT_TOKEN has an invalid format"
[[ "$TELEGRAM_CHAT_ID" =~ ^-?[0-9]+$ ]] \
  || fail "TELEGRAM_CHAT_ID has an invalid format"

host_name="$(hostname -f 2>/dev/null || hostname)"
message="GitLab backup alert: ${FAILED_UNIT} failed on ${host_name} at $(date -u '+%Y-%m-%dT%H:%M:%SZ'). Check: journalctl -u ${FAILED_UNIT}"

# Pass the token through curl stdin so it is not exposed in the process argv.
curl --fail --silent --show-error --output /dev/null \
  --connect-timeout 10 --max-time 30 \
  --retry 3 --retry-delay 2 --retry-all-errors \
  --config - <<CURL_CONFIG
url = "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/sendMessage"
request = "POST"
form-string = "chat_id=${TELEGRAM_CHAT_ID}"
form-string = "text=${message}"
CURL_CONFIG
