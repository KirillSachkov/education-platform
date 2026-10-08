#!/usr/bin/env bash
# Canonical source notices are passed as a BuildKit file secret, outside contexts.
set -euo pipefail
cd "$(dirname "$0")/../.."
tar -cf .image-notices.tar LICENSE ASSET_NOTICE.md THIRD_PARTY_LICENSES frontend/public/fonts/OFL.txt
test -s .image-notices.tar
