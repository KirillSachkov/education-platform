"""Validate explicit production release inputs before transport or host mutation."""

import json
import re

REPOSITORY = "KirillSachkov/education-platform"
PUBLIC_REGISTRY = "ghcr.io/kirillsachkov/education-platform"
LEGACY_REGISTRY = "ghcr.io/kirillsachkov/education-platform-legacy"
LEGACY_SERVICES = (
    "auth-service", "education-service", "file-service", "progress-service",
    "comment-service", "tag-service", "search-service", "access-service",
    "material-processing-service", "notification-service", "telegram-bot-service",
    "trainer-service", "assignment-review-service", "frontend",
)
# Frozen original private roles retain their historical topology for rollback.
SERVICES = tuple(name for name in LEGACY_SERVICES if name != "trainer-service")
HEALTH_SERVICES = tuple(name for name in SERVICES if name != "telegram-bot-service")
LEGACY_HEALTH_SERVICES = tuple(name for name in LEGACY_SERVICES if name != "telegram-bot-service")


def services_for_registry(registry):
    if registry == PUBLIC_REGISTRY:
        return SERVICES
    if registry == LEGACY_REGISTRY:
        return LEGACY_SERVICES
    raise ValueError("unknown release registry")


def strict_json(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("duplicate JSON key")
            result[key] = value
        return result

    def invalid_constant(_):
        raise ValueError("nonfinite JSON value")

    return json.loads(text, object_pairs_hook=pairs, parse_constant=invalid_constant)


def full_sha(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value) or value == "0" * 40:
        raise ValueError("full nonzero source SHA required")
    return value


def digest_reference(value, repository):
    if not isinstance(value, str) or not re.fullmatch(re.escape(repository) + r"@sha256:[0-9a-f]{64}", value):
        raise ValueError("exact registry repository and immutable digest required")
    return value


def dispatch_inputs(operation, release, source_sha="", build_run_id=""):
    if operation not in {"probe", "deploy", "rollback"}:
        raise ValueError("unknown production operation")
    if release not in {"normal-public-build", "current", "previous", "recorded-previous"}:
        raise ValueError("unknown explicit release input")
    if operation == "probe":
        if source_sha or build_run_id or release != "current":
            raise ValueError("probe accepts no release selection")
    elif release == "normal-public-build":
        if operation != "deploy":
            raise ValueError("normal rollback uses the recorded previous manifest")
        full_sha(source_sha)
        if not re.fullmatch(r"[1-9][0-9]{0,19}", str(build_run_id)):
            raise ValueError("explicit image build run required")
    else:
        if source_sha or build_run_id:
            raise ValueError("private release identity must not enter public dispatch inputs")
        if (operation == "deploy" and release != "current") or (operation == "rollback" and release == "current"):
            raise ValueError("operation does not match the selected release")
    return {"operation": operation, "release": release, "source_sha": source_sha, "build_run_id": str(build_run_id)}


def trusted_dispatch(environment):
    if (
        environment.get("GITHUB_REPOSITORY") != REPOSITORY
        or environment.get("GITHUB_REF") != "refs/heads/main"
        or environment.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
    ):
        raise ValueError("production requires a trusted main manual dispatch")
    full_sha(environment.get("GITHUB_SHA"))


def image_manifest(manifest, source_sha, registry=PUBLIC_REGISTRY):
    full_sha(source_sha)
    services = services_for_registry(registry)
    if not isinstance(manifest, dict) or type(manifest.get("schema_version")) is not int or manifest.get("schema_version") != 1 or manifest.get("source_sha") != source_sha:
        raise ValueError("image manifest identity mismatch")
    images = manifest.get("images")
    if not isinstance(images, list) or len(images) != len(services):
        raise ValueError("complete registry-specific image manifest required")
    result = {}
    for row in images:
        if not isinstance(row, dict):
            raise ValueError("image record must be an object")
        name = row.get("name")
        if name not in services or name in result:
            raise ValueError("unknown or duplicate application image")
        reference = digest_reference(row.get("reference"), registry + "/" + name)
        expected = {
            "name": name, "source_sha": source_sha, "tag": registry + "/" + name + ":" + source_sha,
            "digest": reference.split("@", 1)[1], "reference": reference,
        }
        if row != expected:
            raise ValueError("stale or inconsistent image record")
        result[name] = reference
    if set(result) != set(services):
        raise ValueError("missing application image")
    return result


def trusted_build_run(run, source_sha, run_id):
    if (
        run.get("id") != int(run_id) or run.get("head_sha") != full_sha(source_sha)
        or run.get("event") != "push" or run.get("head_branch") != "main"
        or run.get("path") != ".github/workflows/ci.yml"
        or run.get("status") != "completed" or run.get("conclusion") != "success"
        or run.get("repository", {}).get("full_name") != REPOSITORY
        or run.get("head_repository", {}).get("full_name") != REPOSITORY
    ):
        raise ValueError("image build provenance mismatch")


def private_roles(manifest):
    if not isinstance(manifest, dict) or type(manifest.get("schema_version")) is not int or manifest.get("schema_version") != 1 or not isinstance(manifest.get("roles"), dict) or set(manifest["roles"]) != {"current", "previous"}:
        raise ValueError("both immutable approved private roles required")
    digest_reference(manifest.get("postgres_image"), LEGACY_REGISTRY + "/postgres-pgvector")
    for name, role in manifest["roles"].items():
        if not isinstance(role, dict):
            raise ValueError("private role must be an object")
        sha = full_sha(role.get("source_sha"))
        image_manifest(role.get("image_manifest", {}), sha, LEGACY_REGISTRY)
        metadata = role.get("metadata", {})
        if not isinstance(metadata, dict):
            raise ValueError("release metadata must be an object")
        if (
            metadata.get("IMAGE_TAG") != sha or metadata.get("RELEASE_COMMIT_SHA") != sha
            or metadata.get("DOCKER_REGISTRY") != LEGACY_REGISTRY + "/"
            or metadata.get("MEDIA_BINDING_PROTOCOL") != "1"
            or not re.fullmatch(r"[1-9][0-9]*", str(metadata.get("RELEASE_PIPELINE_ID", "")))
            or set(metadata) - {"IMAGE_TAG", "DOCKER_REGISTRY", "RELEASE_COMMIT_SHA", "RELEASE_PIPELINE_ID", "MEDIA_BINDING_PROTOCOL", "RELEASED_AT"}
        ):
            raise ValueError("approved private release metadata mismatch")
        if any("\n" in str(value) or "\r" in str(value) for value in metadata.values()):
            raise ValueError("multiline release metadata refused")
        if not name or role.get("role") != name:
            raise ValueError("private role mismatch")
    hashes = manifest.get("config_hashes", {})
    if not isinstance(hashes, dict) or not hashes or "docker-compose.prod.yml" not in hashes:
        raise ValueError("approved actual configuration identity required")
    for path, sha in hashes.items():
        if (
            not isinstance(path, str) or not isinstance(sha, str)
            or path.startswith("/") or ".." in path.split("/")
            or not (path in {"docker-compose.prod.yml", "nginx.prod.conf"} or path.startswith(("docker/", "scripts/")))
            or not re.fullmatch(r"[0-9a-f]{64}", sha)
        ):
            raise ValueError("invalid approved configuration hash")
    return manifest
