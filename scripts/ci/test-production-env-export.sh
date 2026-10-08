#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

deploy_job="$(awk '
    /^deploy-production:/ { capture = 1 }
    capture && /^  rules:/ { exit }
    capture { print }
' "$ROOT_DIR/.gitlab-ci.yml")"

[[ "$deploy_job" == *'scripts/restore-s3.sh'* ]] ||
    fail "deploy archive does not include restore-s3.sh"
[[ "$deploy_job" == *'/opt/education-platform/scripts/restore-s3.sh'* ]] ||
    fail "deploy does not make restore-s3.sh executable"

umask_line="$(grep -nF 'umask 077' <<<"$deploy_job" | cut -d: -f1)"
export_line="$(grep -nF '> /opt/education-platform/.env.tmp' <<<"$deploy_job" | cut -d: -f1)"
validate_line="$(grep -nF 'test -s /opt/education-platform/.env.tmp' <<<"$deploy_job" | cut -d: -f1)"
validator_line="$(grep -nF '/opt/education-platform/scripts/validate-production-env.sh' <<<"$deploy_job" | tail -n 1 | cut -d: -f1)"
mode_line="$(grep -nF 'chmod 600 /opt/education-platform/.env.tmp' <<<"$deploy_job" | cut -d: -f1)"
promote_line="$(grep -nF 'mv /opt/education-platform/.env.tmp /opt/education-platform/.env' <<<"$deploy_job" | cut -d: -f1)"

[[ -n "$umask_line" && -n "$export_line" && -n "$validate_line" && -n "$validator_line" && -n "$mode_line" && -n "$promote_line" ]] ||
    fail "production env export is missing atomic or permission steps"
((umask_line < export_line && export_line < validate_line && validate_line < validator_line && validator_line < mode_line && mode_line < promote_line)) ||
    fail "production env export steps are out of order"

[[ "$deploy_job" == *'scripts/validate-production-env.sh'* ]] ||
    fail "deploy archive does not include the production env validator"
[[ "$deploy_job" == *"printf '%s\\n%s\\n%s\\n'"* ]] ||
    fail "Infisical credentials are not passed to SSH through stdin"
[[ "$deploy_job" == *'IFS= read -r INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET'* ]] ||
    fail "remote secret export does not read the Infisical secret from stdin"

# shellcheck disable=SC2016 # The literal expansion would expose the secret in argv.
if grep -qF 'INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET=$INFISICAL_CLIENT_SECRET' <<<"$deploy_job"; then
    fail "Infisical client secret is still exposed in the SSH command argv"
fi

if grep -qE '> /opt/education-platform/\.env(["[:space:]]|$)' <<<"$deploy_job"; then
    fail "deploy still writes Infisical export directly to the live .env"
fi

printf 'Production env export contract tests passed.\n'
