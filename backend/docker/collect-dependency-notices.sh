#!/usr/bin/env sh
# Preserve package metadata, license and notice files from this image's restore.
set -eu
notice_output="${IMAGE_NOTICES_OUTPUT:-/app/dependency-notices}"
mkdir -p "$notice_output/nuget"
cd "${NUGET_PACKAGES:-/root/.nuget/packages}"
files="$(mktemp)"
archive="$(mktemp)"
trap 'rm -f "$files" "$archive"' EXIT
find . -type f \( -iname '*.nuspec' -o -iname '*license*' -o -iname '*notice*' -o -iname '*copyright*' \) \
  -print0 > "$files"
tar --null -T "$files" -cf "$archive"
tar -xf "$archive" -C "$notice_output/nuget"
