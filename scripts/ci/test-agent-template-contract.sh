#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PLATFORM_OUTPUT="$(mktemp -d "$ROOT_DIR/backend/.agent-template-contract-platform.XXXXXX")"
SLICE_OUTPUT="$(mktemp -d "$ROOT_DIR/backend/.agent-template-contract-slice.XXXXXX")"
HIVE_BASE="${TMPDIR:-/tmp}"
HIVE_DIR="$(mktemp -d "$HIVE_BASE/agent-template-hive.XXXXXX")"

cleanup() {
  local output
  for output in "$PLATFORM_OUTPUT" "$SLICE_OUTPUT"; do
    case "$output" in
      "$ROOT_DIR"/backend/.agent-template-contract-*) rm -rf -- "$output" ;;
    esac
  done
  case "$HIVE_DIR" in
    "$HIVE_BASE"/agent-template-hive.*) rm -rf -- "$HIVE_DIR" ;;
  esac
}
trap cleanup EXIT

dotnet new install "$ROOT_DIR/.templates/platform-service" --debug:custom-hive "$HIVE_DIR" >/dev/null
dotnet new install "$ROOT_DIR/.templates/vertical-slice-service" --debug:custom-hive "$HIVE_DIR" >/dev/null

dotnet new platform-service \
  --debug:custom-hive "$HIVE_DIR" \
  --name AgentTemplatePlatform \
  --port 8097 \
  --schema agenttemplateplatform \
  --output "$PLATFORM_OUTPUT" >/dev/null
dotnet new vertical-slice-service \
  --debug:custom-hive "$HIVE_DIR" \
  --name AgentTemplateSlice \
  --port 8098 \
  --schema agenttemplateslice \
  --output "$SLICE_OUTPUT" >/dev/null

for output in "$PLATFORM_OUTPUT" "$SLICE_OUTPUT"; do
  test -s "$output/AGENTS.md"
  printf '@AGENTS.md\n' | cmp -s - "$output/CLAUDE.md"
done

RESTORE_ARGS=(--verbosity minimal)
if [[ -f "$ROOT_DIR/backend/nuget.config" ]]; then
  RESTORE_ARGS+=(--configfile "$ROOT_DIR/backend/nuget.config")
fi
dotnet restore "$PLATFORM_OUTPUT/AgentTemplatePlatform.slnx" \
  "${RESTORE_ARGS[@]}"
dotnet restore "$SLICE_OUTPUT/AgentTemplateSlice.slnx" \
  "${RESTORE_ARGS[@]}"
dotnet build "$PLATFORM_OUTPUT/AgentTemplatePlatform.slnx" \
  --configuration Release --no-restore --verbosity minimal
dotnet build "$SLICE_OUTPUT/AgentTemplateSlice.slnx" \
  --configuration Release --no-restore --verbosity minimal

echo "agent template generation and build contract: ok"
