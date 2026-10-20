#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

created_env=false
if [[ ! -e .env ]]; then
  ln -s .env.example .env
  created_env=true
fi

dev_json="$(mktemp)"
prod_json="$(mktemp)"

cleanup() {
  rm -f "$dev_json" "$prod_json"
  if [[ "$created_env" == true ]]; then
    rm -f .env
  fi
}
trap cleanup EXIT

if grep -nE '\$\{DOCKER_REGISTRY:-\}|\$\{IMAGE_TAG:-latest\}' docker-compose.prod.yml; then
  echo "Production service images must require an explicit registry and immutable release tag." >&2
  exit 1
fi

if grep -nF '${POSTGRES_EXPORTER_PASSWORD:-exporter}' docker-compose.prod.yml; then
  echo "Production monitoring credentials must not have a default password." >&2
  exit 1
fi

IMAGE_TAG=contract-test docker compose --profile '*' -f docker-compose.yml config --format json >"$dev_json"
DOCKER_REGISTRY=registry.example/ IMAGE_TAG=contract-test POSTGRES_EXPORTER_PASSWORD=contract-test RESTORE_POSTGRES_IMAGE=pgvector/pgvector:pg16 \
  docker compose --profile '*' -f docker-compose.prod.yml config --format json >"$prod_json"

for rendered_compose in "$dev_json" "$prod_json"; do
  jq -e '.services | has("trainer-service") == false and has("trainer-service-migrations") == false' \
    "$rendered_compose" >/dev/null
  jq -e '
    .services["comment-service-migrations"].depends_on["education-service-migrations"].condition == "service_completed_successfully"
    and .services["comment-service"].depends_on["education-service-migrations"].condition == "service_completed_successfully"
    and .services["comment-service"].depends_on["comment-service-migrations"].condition == "service_completed_successfully"
  ' "$rendered_compose" >/dev/null
done

# The normal production runtime has no monitoring storage or remote exporters.
jq -e '
  [.services | keys[] | select(. == "infisical" or . == "tempo" or . == "loki" or . == "prometheus" or . == "otel-collector" or . == "alloy" or . == "grafana" or . == "postgres-exporter" or . == "redis-exporter")] | length == 0
' "$prod_json" >/dev/null
jq -e '
  [.services[] | select(.environment.ASPNETCORE_ENVIRONMENT == "Production")
   | (.environment.OTEL_SDK_DISABLED == "true" and .environment.OTEL_EXPORTER_OTLP_ENDPOINT == "")] | all
' "$prod_json" >/dev/null

echo "Compose contracts valid: migration dependencies preserved; normal production telemetry disabled."
