#!/usr/bin/env bash
# Mirror Testcontainers base images from Docker Hub into the project's GitLab Container
# Registry. CI integration tests then pull them via TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX
# (= ${CI_REGISTRY_IMAGE}/) instead of Docker Hub — eliminates the anonymous Docker Hub
# pull-rate-limit (`toomanyrequests`) that flaked the radiance runner (#404 / #407).
#
# Re-run ONLY when bumping an image version. Keep this list in sync with:
#   - the pre-pull list in .gitlab-ci.yml  →  .integration-test-base
#   - the image versions pinned in the test WebFactories (PostgreSqlBuilder.WithImage(...) etc.)
#
# Usage:  GITLAB_TOKEN=<PAT with write_registry> [GITLAB_USER=root] ./scripts/ci-mirror-images.sh
# Requires: docker (runs skopeo in a container — preserves multi-arch manifest lists, so the
#           amd64 CI runner gets the right arch regardless of the machine running this script).
set -euo pipefail

REGISTRY="${REGISTRY:-gitlab-sachkov.ru:5050}"
PROJECT_PATH="${PROJECT_PATH:-miracle-generation/education-platform}"
DEST="$REGISTRY/$PROJECT_PATH"
GL_USER="${GITLAB_USER:-root}"
: "${GITLAB_TOKEN:?set GITLAB_TOKEN to a GitLab PAT with write_registry scope}"

# "<docker-hub source>|<dest tag under $DEST>". The dest path mirrors the hub image name
# 1:1 so TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX="${CI_REGISTRY_IMAGE}/" resolves correctly:
# e.g. Testcontainers turns `postgres:16-alpine` → `${CI_REGISTRY_IMAGE}/postgres:16-alpine`.
# NB: the GitLab registry rejects repo paths deeper than 4 segments after the host
# (".../education-platform/<org>/<image>" is the max) — do NOT introduce a sub-dir like
# `ci-mirror/`, that pushes org-namespaced images (pgvector/pgvector) to 5 segments → 403.
images=(
  "docker.io/library/postgres:16-alpine|postgres:16-alpine"
  "docker.io/pgvector/pgvector:pg16|pgvector/pgvector:pg16"
  "docker.io/minio/minio:latest|minio/minio:latest"
  "docker.io/library/redis:7.2|redis:7.2"
  "docker.io/library/redis:7-alpine|redis:7-alpine"
  "docker.io/typesense/typesense:30.1|typesense/typesense:30.1"
  "docker.io/library/rabbitmq:3-management|rabbitmq:3-management"
)

for pair in "${images[@]}"; do
  src="${pair%%|*}"
  dst="${pair##*|}"
  echo ">> $src  ->  $DEST/$dst"
  docker run --rm quay.io/skopeo/stable copy --all --retry-times 3 \
    --dest-creds "$GL_USER:$GITLAB_TOKEN" \
    "docker://$src" "docker://$DEST/$dst"
done

echo "Mirrored ${#images[@]} images into $DEST"
