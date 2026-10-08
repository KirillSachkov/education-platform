#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/scripts/ci" "$fixture/frontend" "$fixture/bin"
cp "$root/scripts/ci/prettier-check-diff.sh" "$fixture/scripts/ci/"
cp "$root/scripts/ci/github-bootstrap-formatting.py" "$fixture/scripts/ci/"
cd "$fixture"
git init -q -b main
git config user.email ci@example.invalid
git config user.name 'CI fixture'
printf 'original\n' > README.md
git add . && git commit -qm fixture
base="$(git rev-parse HEAD)"
if bash scripts/ci/prettier-check-diff.sh > /dev/null 2>&1; then
  echo 'Missing base was silently accepted' >&2; exit 1
fi
if CI_DIFF_BASE=ffffffffffffffffffffffffffffffffffffffff bash scripts/ci/prettier-check-diff.sh > /dev/null 2>&1; then
  echo 'Unavailable base was silently accepted' >&2; exit 1
fi
mkdir -p 'frontend/src/app/(group)'
printf 'export {};\n' > 'frontend/src/app/(group)/file with spaces.ts'
git add . && git commit -qm frontend
cat > bin/npx <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$ARGUMENTS_FILE"
exit "${FAKE_STATUS:-0}"
SH
chmod +x bin/npx
export PATH="$fixture/bin:$PATH" ARGUMENTS_FILE="$fixture/arguments"
CI_DIFF_BASE="$base" CI_DIFF_HEAD="$(git rev-parse HEAD)" bash scripts/ci/prettier-check-diff.sh
printf '%s\n' prettier --check 'src/app/(group)/file with spaces.ts' > expected
cmp expected arguments
if FAKE_STATUS=1 CI_DIFF_BASE="$base" bash scripts/ci/prettier-check-diff.sh >/dev/null 2>&1; then
  echo 'Prettier failure was ignored' >&2; exit 1
fi
echo 'Explicit Prettier base and filename boundary tests passed.'
