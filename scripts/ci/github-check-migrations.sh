#!/usr/bin/env bash
# Public PR authors cannot authorize a migration bypass through commit text.
set -euo pipefail
cd "$(dirname "$0")/../.."
: "${CI_DIFF_BASE:?explicit selection base required}"
[[ "$CI_DIFF_BASE" =~ ^[0-9a-f]{40}$ ]] || { echo 'Invalid diff base' >&2; exit 1; }
git cat-file -e "$CI_DIFF_BASE"
: "${CI_DIFF_HEAD:?explicit diff head required}"
[[ "$CI_DIFF_HEAD" =~ ^[0-9a-f]{40}$ ]] || { echo 'Invalid diff head' >&2; exit 1; }
git cat-file -e "$CI_DIFF_HEAD^{commit}"
changes="$(git diff --no-renames --name-status --diff-filter=DM "$CI_DIFF_BASE" "$CI_DIFF_HEAD" -- \
  ':(glob)backend/**/src/**/Migrations/*.cs' ':(glob)backend/**/src/**/DataMigrations/*.cs')"
# Handwritten tests may live under tests/.../Migrations; they are executable
# verification, not committed deployment history.
# EF updates the model snapshot when adding a corrective migration. The existing
# migration and its designer stay immutable; snapshot deletion is still rejected.
violations="$(printf '%s\n' "$changes" | awk '!($1 == "M" && $2 ~ /ModelSnapshot.cs$/) && NF')"
if [ -n "$violations" ]; then
  printf 'ERROR: committed migrations are immutable. Create a corrective migration.\n%s\n' "$violations" >&2
  exit 1
fi
echo 'Migration immutability passed.'
