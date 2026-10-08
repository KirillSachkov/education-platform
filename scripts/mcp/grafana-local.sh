#!/usr/bin/env bash
# MCP wrapper: launches native mcp-grafana binary against local dev grafana.
#
# Talks to grafana on the host port (localhost:3001 → grafana:3000 inside compose).
# Requires: `./scripts/dev.sh up-obs` (or `up-all`) to be running, and
# `brew install mcp-grafana` (or any other install of the binary on PATH).
set -e

if ! command -v mcp-grafana >/dev/null 2>&1; then
  echo "mcp-grafana not found on PATH. Install: brew install mcp-grafana" >&2
  exit 1
fi

export GRAFANA_URL="${GRAFANA_URL:-http://localhost:3001}"
export GRAFANA_USERNAME="${GRAFANA_USERNAME:-admin}"
export GRAFANA_PASSWORD="${GRAFANA_PASSWORD:-admin}"

exec mcp-grafana -t stdio
