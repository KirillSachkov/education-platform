#!/bin/bash
# Diff-scoped Prettier gate (#712): проверяет ТОЛЬКО файлы, изменённые в MR,
# против базы диффа. Полный `prettier --check .` сейчас красный на ~537
# исторических файлах — их чинит gc-sweep порциями, а этот гейт гарантирует,
# что новые/изменённые файлы всегда отформатированы («clean as you touch»).
set -euo pipefail
cd "$(dirname "$0")/../.."

BASE="${CI_DIFF_BASE:-${CI_MERGE_REQUEST_DIFF_BASE_SHA:-}}"
HEAD_SHA="${CI_DIFF_HEAD:-HEAD}"
[[ "$BASE" =~ ^[0-9a-f]{40}$ ]] || { echo 'prettier-check-diff: explicit full diff base is required' >&2; exit 1; }
git cat-file -e "$BASE"
git cat-file -e "$HEAD_SHA^{commit}"

CHANGED_FILE="$(mktemp)"
FILTERED_FILE="$(mktemp)"
trap 'rm -f "$CHANGED_FILE" "$FILTERED_FILE"' EXIT
git diff --name-only -z --diff-filter=ACMR "$BASE" "$HEAD_SHA" -- \
  ':(glob)frontend/**/*.ts' ':(glob)frontend/**/*.tsx' ':(glob)frontend/**/*.js' ':(glob)frontend/**/*.jsx' \
  ':(glob)frontend/**/*.mjs' ':(glob)frontend/**/*.cjs' ':(glob)frontend/**/*.json' ':(glob)frontend/**/*.css' ':(glob)frontend/**/*.md' > "$CHANGED_FILE"
export CI_DIFF_BASE="$BASE" CI_DIFF_HEAD="$HEAD_SHA"
python3 scripts/ci/github-bootstrap-formatting.py < "$CHANGED_FILE" > "$FILTERED_FILE"
CHANGED=()
while IFS= read -r -d '' path; do CHANGED+=("${path#frontend/}"); done < "$FILTERED_FILE"
[ "${#CHANGED[@]}" -eq 0 ] && { echo "prettier-check-diff: нет изменённых frontend-файлов"; exit 0; }

cd frontend
if ! npx prettier --check "${CHANGED[@]}"; then
  echo ""
  echo "Fix: cd frontend && npx prettier --write <файлы выше>, затем закоммить."
  exit 1
fi
echo "prettier-check-diff: OK (${#CHANGED[@]} файлов)"
