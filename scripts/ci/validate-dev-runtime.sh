#!/usr/bin/env bash
set -euo pipefail

check_url() {
  local name="$1"
  local url="$2"
  local attempt
  for attempt in $(seq 1 30); do
    if curl --fail --silent --show-error --max-time 5 --output /dev/null "$url"; then
      echo "OK: $name ($url)"
      return 0
    fi
    sleep 2
  done
  echo "ERROR: $name did not become healthy after 60 seconds ($url)" >&2
  return 1
}

service_count=0
while IFS='|' read -r service port; do
  check_url "$service live" "http://localhost:${port}/health/live"
  check_url "$service ready" "http://localhost:${port}/health/ready"
  service_count=$((service_count + 1))
done <<EOF
EducationContentService|${EDUCATION_HOST_PORT:-8001}
FileService|${FILE_HOST_PORT:-8002}
ProgressService|${PROGRESS_HOST_PORT:-8003}
CommentService|${COMMENT_HOST_PORT:-8004}
AuthService|${AUTH_HOST_PORT:-8005}
NotificationService|${NOTIFICATION_HOST_PORT:-8006}
TelegramBotService|${TELEGRAM_HOST_PORT:-8008}
AccessService|${ACCESS_HOST_PORT:-8010}
MaterialProcessingService|${MATERIAL_PROCESSING_HOST_PORT:-8011}
AssignmentReviewService|${ASSIGNMENT_REVIEW_HOST_PORT:-8012}
EOF

nginx_port="${NGINX_HOST_PORT:-80}"
nginx_origin="http://localhost"
if [[ "$nginx_port" != "80" ]]; then
  nginx_origin="http://localhost:${nginx_port}"
fi

check_url "frontend through nginx" "${nginx_origin}/"
check_url "frontend health through nginx" "${nginx_origin}/api/health"
check_url "OIDC discovery through nginx" "${nginx_origin}/.well-known/openid-configuration"
check_url "anonymous course catalog through nginx" "${nginx_origin}/api/courses/catalog/?limit=20"
check_url "anonymous platform stats through nginx" "${nginx_origin}/api/courses/platform-stats/"

echo "Dev runtime certification passed: $service_count services live+ready and 5 nginx golden paths"
