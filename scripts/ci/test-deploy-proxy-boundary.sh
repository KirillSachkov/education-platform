#!/usr/bin/env bash

set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ci_config="${CI_CONFIG_UNDER_TEST:-$repo_root/.gitlab-ci.yml}"

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

extract_block() {
  local header="$1"
  awk -v header="$header" '
    $0 == header {
      capture = 1
      start = NR
    }
    capture && NR > start && /^[^[:space:]#][^:]*:/ {
      exit
    }
    capture {
      print
    }
  ' "$ci_config"
}

route_block="$(extract_block '.prod-ssh-route: &prod-ssh-route')"
build_block="$(extract_block '.docker-build:')"
deploy_block="$(extract_block 'deploy-production:')"
rollback_block="$(extract_block 'rollback-production:')"
validation_block="$(extract_block 'validate-test-rules:')"

[[ -n "$route_block" ]] || fail "missing .prod-ssh-route anchor"
[[ -n "$build_block" ]] || fail "missing .docker-build template"
[[ -n "$deploy_block" ]] || fail "missing deploy-production job"
[[ -n "$rollback_block" ]] || fail "missing rollback-production job"
[[ -n "$validation_block" ]] || fail "missing validate-test-rules job"

grep -Fq 'DEPLOY_CONNECT_PROXY' <<<"$route_block" ||
  fail ".prod-ssh-route does not use the dedicated deploy proxy variable"
if grep -Fq 'HTTP_PROXY' <<<"$route_block"; then
  fail ".prod-ssh-route still depends on the runner build proxy"
fi
grep -Fq '${DEPLOY_CONNECT_PROXY:-http://172.17.0.1:3129}' <<<"$route_block" ||
  fail "deploy proxy fallback is not 172.17.0.1:3129"
grep -Eq '^[[:space:]]+- bash scripts/ci/test-deploy-proxy-boundary\.sh$' <<<"$route_block" ||
  fail "production SSH route does not run the proxy boundary preflight"

grep -Fq '"$HTTP_PROXY" "$HTTPS_PROXY" > ~/.docker/config.json' <<<"$build_block" ||
  fail ".docker-build no longer writes the runner HTTP/HTTPS proxy pair"
if grep -Fq 'DEPLOY_CONNECT_PROXY' <<<"$build_block"; then
  fail ".docker-build must not consume the dedicated deploy proxy"
fi

grep -Eq '^[[:space:]]+- \*prod-ssh-route$' <<<"$deploy_block" ||
  fail "deploy-production does not consume the shared production SSH route"
grep -Eq '^[[:space:]]+- \*prod-ssh-route$' <<<"$rollback_block" ||
  fail "rollback-production does not consume the shared production SSH route"
route_consumers="$(grep -Ec '^[[:space:]]+- \*prod-ssh-route$' "$ci_config")"
[[ "$route_consumers" == "2" ]] ||
  fail "production SSH route must have exactly deploy and rollback consumers"

grep -Eq '^[[:space:]]+- bash scripts/ci/test-deploy-proxy-boundary\.sh$' <<<"$validation_block" ||
  fail "validate-test-rules does not execute the deploy proxy contract"
grep -Eq '^[[:space:]]+- scripts/ci/test-deploy-proxy-boundary\.sh$' <<<"$validation_block" ||
  fail "validate-test-rules changes do not include the deploy proxy contract"

printf 'Deploy/build proxy boundary contract passed.\n'
