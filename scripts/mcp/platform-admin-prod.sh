#!/usr/bin/env bash
# MCP wrapper: launches the platform admin MCP server pointed at production.
# Loads env from mcp/admin-server/.env.prod (gitignored).
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "${SCRIPT_DIR}/../.." && pwd )"
ENV_FILE="${REPO_ROOT}/mcp/admin-server/.env.prod"
ENTRY="${REPO_ROOT}/mcp/admin-server/dist/index.js"

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "missing ${ENV_FILE} — copy .env.example and fill in prod secret" >&2
  exit 1
fi

if [[ ! -f "${ENTRY}" ]]; then
  echo "missing ${ENTRY} — run 'cd mcp/admin-server && npm install && npm run build'" >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

exec node "${ENTRY}"
