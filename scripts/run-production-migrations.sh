#!/usr/bin/env bash

set -Eeuo pipefail

compose_file="${1:-docker-compose.prod.yml}"
current_release_file="${2:-/opt/education-platform/releases/current.env}"
docker_bin="${DOCKER_BIN:-docker}"
topology="${3:-source}"
case "$topology" in
    source|legacy) ;;
    *) printf 'FATAL: unknown release topology.\n' >&2; exit 1 ;;
esac

migration_services=(
    auth-service-migrations
    education-service-migrations
    file-service-migrations
    progress-service-migrations
    comment-service-migrations
    access-service-migrations
    material-processing-service-migrations
    notification-service-migrations
    telegram-bot-service-migrations
    assignment-review-service-migrations
)

remaining_cutover_services=(
    progress-service-migrations
    comment-service-migrations
    access-service-migrations
    material-processing-service-migrations
    notification-service-migrations
    telegram-bot-service-migrations
    assignment-review-service-migrations
)

# Frozen legacy roles use their original migration inventory.
if [[ "$topology" == legacy ]]; then
    migration_services=(
        auth-service-migrations education-service-migrations file-service-migrations
        progress-service-migrations comment-service-migrations tag-service-migrations
        search-service-migrations access-service-migrations material-processing-service-migrations
        notification-service-migrations telegram-bot-service-migrations trainer-service-migrations
        assignment-review-service-migrations
    )
    remaining_cutover_services=("${migration_services[@]:3}")
fi

run_migrations() {
    local service

    for service in "$@"; do
        printf 'Migration preflight: %s\n' "$service"
        if ! "$docker_bin" compose -f "$compose_file" run --rm --no-deps --no-TTY "$service"; then
            printf 'FATAL: migration preflight failed: %s\n' "$service" >&2
            exit 1
        fi
    done
}

printf 'Running production migration preflight...\n'

if [[ -f "$current_release_file" ]] &&
    grep -q '^MEDIA_BINDING_PROTOCOL=1$' "$current_release_file"; then
    run_migrations "${migration_services[@]}"
else
    printf 'First media-binding cutover: quiescing the old FileService.\n'
    "$docker_bin" compose -f "$compose_file" stop file-service

    run_migrations auth-service-migrations education-service-migrations

    printf 'Starting revision-aware AuthService and EducationContentService...\n'
    if ! "$docker_bin" compose -f "$compose_file" up -d --force-recreate --no-deps \
        --wait --wait-timeout 180 auth-service education-service; then
        printf 'FATAL: revision-aware AuthService/EducationContentService did not become healthy.\n' >&2
        exit 1
    fi

    run_migrations file-service-migrations
    run_migrations "${remaining_cutover_services[@]}"
fi

printf 'Production migration preflight completed successfully.\n'
