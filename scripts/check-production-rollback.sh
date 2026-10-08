#!/usr/bin/env bash

set -Eeuo pipefail

current_release_file="${1:?current release metadata path is required}"
previous_release_file="${2:?previous release metadata path is required}"
pending_release_file="${3:-}"

[[ -f "$current_release_file" ]] || {
    printf 'Current release metadata is missing: %s\n' "$current_release_file" >&2
    exit 1
}
[[ -f "$previous_release_file" ]] || {
    printf 'Previous release metadata is missing: %s\n' "$previous_release_file" >&2
    exit 1
}

has_media_binding_protocol() {
    local release_file="$1"

    [[ -n "$release_file" && -f "$release_file" ]] &&
        grep -q '^MEDIA_BINDING_PROTOCOL=1$' "$release_file"
}

if { has_media_binding_protocol "$current_release_file" ||
    has_media_binding_protocol "$pending_release_file"; } &&
    ! has_media_binding_protocol "$previous_release_file"; then
    printf '%s\n' \
        'Rollback blocked: previous image is incompatible with revisioned media schema.' \
        'Restore the matching pre-deploy database backup or ship a forward fix.' >&2
    exit 1
fi
