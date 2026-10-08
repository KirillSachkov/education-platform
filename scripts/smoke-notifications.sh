#!/usr/bin/env bash
#
# E2E smoke test для NotificationService.
#
# Проверяет полный flow:
#   1. OTP-регистрация юзера (welcome-notification должна упасть в inbox + email)
#   2. GET /notifications/ — inbox содержит Welcome
#   3. GET /notifications/unread-count/ — > 0
#   4. POST /notifications/{id}/read/ — mark-as-read
#   5. GET /notifications/unread-count/ — уменьшилось
#   6. GET /notifications/preferences/ — дефолты возвращаются
#   7. PUT /notifications/preferences/ — меняем флаги + opt-out, читаем назад
#   8. GET /n/{id} — proxy endpoint → 302 redirect
#
# Usage:
#   BASE_URL=http://localhost scripts/smoke-notifications.sh
#   BASE_URL=https://sachkov-learn.net scripts/smoke-notifications.sh
#
# Требует: curl, jq. Оба должны быть в $PATH.
#
# Exit 0 = всё OK. Exit 1 = любой шаг зафейлился.
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost}"
EMAIL="${SMOKE_EMAIL:-smoke+$(date +%s)@sachkov-learn.local}"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

log() { echo "[smoke] $*" >&2; }
fail() { echo "[smoke] FAIL: $*" >&2; exit 1; }

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}
require_cmd curl
require_cmd jq

# -- 1. OTP send --
log "Step 1: POST /auth/otp/send (email=$EMAIL)"
curl -fsS -X POST "$BASE_URL/auth/otp/send" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\"}" >/dev/null || fail "OTP send failed"

# В dev Mailpit можно вытянуть OTP через API. Для prod — ручной шаг.
# Здесь предполагается dev: Mailpit API по 8025, последний email.
if [[ "$BASE_URL" == *"localhost"* ]]; then
  log "Step 1a: fetching OTP from Mailpit"
  # Mailpit API: GET /api/v1/search?query=to:EMAIL → messages[].ID → GET /api/v1/message/{id}
  MAILPIT="http://localhost:8025"
  sleep 1
  MSG_ID="$(curl -fsS "$MAILPIT/api/v1/search?query=to:%22$EMAIL%22&limit=1" | jq -r '.messages[0].ID // empty')"
  [[ -n "$MSG_ID" ]] || fail "OTP email not found in Mailpit"
  OTP="$(curl -fsS "$MAILPIT/api/v1/message/$MSG_ID" | jq -r '.Text // .HTML' | grep -oE '[0-9]{6}' | head -1)"
  [[ -n "$OTP" ]] || fail "OTP code not parsed from email"
else
  read -rp "Enter OTP from email to $EMAIL: " OTP
fi

# -- 2. Verify OTP → получаем access_token --
log "Step 2: POST /auth/otp/verify"
TOKEN_RESPONSE="$(curl -fsS -X POST "$BASE_URL/auth/otp/verify" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"code\":\"$OTP\"}")"
TOKEN="$(echo "$TOKEN_RESPONSE" | jq -r '.result.accessToken // .accessToken // empty')"
[[ -n "$TOKEN" ]] || fail "accessToken not found in verify response: $TOKEN_RESPONSE"
AUTH_HEADER="Authorization: Bearer $TOKEN"

# -- 3. Inbox: дать Wolverine время обработать UserCreated → welcome --
log "Step 3: waiting 5s for UserCreated handler to produce welcome notification"
sleep 5

INBOX="$(curl -fsS "$BASE_URL/api/notifications/" -H "$AUTH_HEADER")"
TOTAL="$(echo "$INBOX" | jq -r '.result.items | length')"
[[ "$TOTAL" -ge 1 ]] || fail "Inbox empty after signup (expected welcome notification): $INBOX"
log "  → inbox has $TOTAL notification(s)"

WELCOME_ID="$(echo "$INBOX" | jq -r '.result.items[] | select(.type == 1) | .id' | head -1)"
[[ -n "$WELCOME_ID" ]] || fail "Welcome notification (type=1) not found: $INBOX"

# -- 4. Unread count --
log "Step 4: GET /notifications/unread-count/"
UNREAD_BEFORE="$(curl -fsS "$BASE_URL/api/notifications/unread-count/" -H "$AUTH_HEADER" | jq -r '.result.count')"
[[ "$UNREAD_BEFORE" -ge 1 ]] || fail "unread-count = $UNREAD_BEFORE, expected >= 1"
log "  → unread = $UNREAD_BEFORE"

# -- 5. Mark as read --
log "Step 5: POST /notifications/$WELCOME_ID/read/"
curl -fsS -X POST "$BASE_URL/api/notifications/$WELCOME_ID/read/" -H "$AUTH_HEADER" >/dev/null

UNREAD_AFTER="$(curl -fsS "$BASE_URL/api/notifications/unread-count/" -H "$AUTH_HEADER" | jq -r '.result.count')"
[[ "$UNREAD_AFTER" -eq $((UNREAD_BEFORE - 1)) ]] \
  || fail "unread didn't decrement: before=$UNREAD_BEFORE after=$UNREAD_AFTER"
log "  → unread = $UNREAD_AFTER (decremented)"

# -- 6. Preferences: GET default --
log "Step 6: GET /notifications/preferences/"
PREFS="$(curl -fsS "$BASE_URL/api/notifications/preferences/" -H "$AUTH_HEADER")"
TG_ENABLED="$(echo "$PREFS" | jq -r '.result.telegramEnabled')"
EMAIL_ENABLED="$(echo "$PREFS" | jq -r '.result.emailEnabled')"
[[ "$TG_ENABLED" == "true" ]] || fail "default telegramEnabled should be true, got $TG_ENABLED"
[[ "$EMAIL_ENABLED" == "true" ]] || fail "default emailEnabled should be true, got $EMAIL_ENABLED"
log "  → defaults ok (tg=true, email=true)"

# -- 7. Preferences: PUT opt-out CourseEnrolled (type=2) --
log "Step 7: PUT /notifications/preferences/ — opt-out CourseEnrolled"
curl -fsS -X PUT "$BASE_URL/api/notifications/preferences/" \
  -H "$AUTH_HEADER" -H 'Content-Type: application/json' \
  -d '{"telegramEnabled":false,"emailEnabled":true,"optedOutTypes":[2]}' >/dev/null

UPDATED="$(curl -fsS "$BASE_URL/api/notifications/preferences/" -H "$AUTH_HEADER" | jq -r '.result.optedOutTypes[0]')"
[[ "$UPDATED" == "2" ]] || fail "opt-out didn't persist: expected 2, got $UPDATED"
log "  → opt-out saved (CourseEnrolled=2)"

# -- 8. Proxy /n/{id} → 302 --
log "Step 8: GET /n/$WELCOME_ID — follow 302 no"
STATUS="$(curl -o /dev/null -sS -w '%{http_code}' "$BASE_URL/n/$WELCOME_ID" -H "$AUTH_HEADER" 2>&1)"
[[ "$STATUS" == "302" || "$STATUS" == "301" ]] \
  || fail "/n/{id} expected 302, got $STATUS"
log "  → proxy redirect ok ($STATUS)"

log ""
log "✓ All smoke steps passed."
