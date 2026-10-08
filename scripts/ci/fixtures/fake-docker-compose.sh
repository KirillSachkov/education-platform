#!/usr/bin/env bash

set -Eeuo pipefail

printf '%s\n' "$*" >> "${FAKE_DOCKER_LOG:?FAKE_DOCKER_LOG must be set}"

service_name="${!#}"
if [[ "$service_name" == "${FAKE_DOCKER_FAIL_SERVICE:-}" ]]; then
    exit 42
fi
