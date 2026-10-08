#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
[[ "${CI_DIFF_BASE:-}" =~ ^[0-9a-f]{40}$ ]] || { echo 'Whitespace requires an explicit full diff base' >&2; exit 1; }
[[ "${CI_DIFF_HEAD:-}" =~ ^[0-9a-f]{40}$ ]] || { echo 'Whitespace requires an explicit full diff head' >&2; exit 1; }
git cat-file -e "$CI_DIFF_BASE"
git cat-file -e "$CI_DIFF_HEAD^{commit}"
changed_file="$(mktemp)"
filtered_file="$(mktemp)"
trap 'rm -f "$changed_file" "$filtered_file"' EXIT
git diff --no-renames --name-only -z --diff-filter=ACMR "$CI_DIFF_BASE" "$CI_DIFF_HEAD" > "$changed_file"
python3 scripts/ci/github-bootstrap-formatting.py --tool whitespace < "$changed_file" > "$filtered_file"
changed=()
while IFS= read -r -d '' path; do changed+=("$path"); done < "$filtered_file"
if [ "${#changed[@]}" -gt 0 ]; then
  git --literal-pathspecs -c core.whitespace=cr-at-eol diff --check "$CI_DIFF_BASE" "$CI_DIFF_HEAD" -- "${changed[@]}"
fi
echo "Whitespace: ${#changed[@]} changed inputs checked."
