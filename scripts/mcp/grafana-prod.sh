#!/usr/bin/env bash
# MCP wrapper: ensures SSH tunnel to prod Grafana, then launches native mcp-grafana.
# Tunnel: localhost:3002 -> prod:3001
# Requires: SSH access to prod, GRAFANA_API_KEY_PROD env var, mcp-grafana on PATH.
set -e

TUNNEL_PORT=3002
PROD_HOST="${PROD_SSH_HOST:?set PROD_SSH_HOST to the SSH destination}"
PROD_GRAFANA_PORT=3001

if ! command -v mcp-grafana >/dev/null 2>&1; then
  echo "mcp-grafana not found on PATH. Install: brew install mcp-grafana" >&2
  exit 1
fi

# Create SSH tunnel if not already running
if ! lsof -i "tcp:${TUNNEL_PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
  ssh -f -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 \
    -L "${TUNNEL_PORT}:localhost:${PROD_GRAFANA_PORT}" "${PROD_HOST}"
fi

export GRAFANA_URL="http://localhost:${TUNNEL_PORT}"
export GRAFANA_SERVICE_ACCOUNT_TOKEN="${GRAFANA_API_KEY_PROD}"

exec mcp-grafana -t stdio
