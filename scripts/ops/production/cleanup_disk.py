#!/usr/bin/env python3
"""Plan narrowly owned image/archive retention; deletion requires an accepted plan hash."""

import argparse
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import time

from release_model import PUBLIC_REGISTRY, LEGACY_REGISTRY, LEGACY_SERVICES, image_manifest, strict_json


def private_json(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor) as stream:
        metadata = os.fstat(stream.fileno())
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != os.geteuid() or metadata.st_mode & 0o077:
            raise ValueError("root-private cleanup input required")
        body = stream.read(16 * 1024 * 1024 + 1)
    if len(body) > 16 * 1024 * 1024:
        raise ValueError("cleanup input too large")
    return strict_json(body)


def command(arguments, *, env=None):
    result = subprocess.run(arguments, env=env, capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError("retention command failed; stop and inspect private evidence")
    return result.stdout


def repository(reference):
    base = reference.split("@")[0]
    return base.rsplit(":", 1)[0] if ":" in base.rsplit("/", 1)[-1] else base


def protected_references(root, config):
    references = set(config.get("independent_images", []))
    for name in ["current", "previous", "pending"]:
        path = root / ".github-production" / (name + ".json")
        if name == "pending" and not path.exists():
            continue
        record = private_json(path)
        references.update(image_manifest(record["image_manifest"], record["source_sha"], record["registry"]).values())
        references.add(record["postgres_image"])
    if not references or any(not re.fullmatch(r"[^\s]+@sha256:[0-9a-f]{64}", value) for value in references):
        raise ValueError("immutable recovery references required")
    return references


def inventory(root, config, now=None):
    if (root / ".github-production/promotion.pending.json").exists():
        raise ValueError("interrupted promotion requires recovery before cleanup")
    now = time.time() if now is None else now
    allowed = {registry + "/" + service for registry in [PUBLIC_REGISTRY, LEGACY_REGISTRY] for service in LEGACY_SERVICES}
    extra = config.get("legacy_repositories", [])
    if not isinstance(extra, list) or any(not isinstance(item, str) or not re.fullmatch(r"[a-z0-9][a-z0-9.:_/-]+/[a-z0-9][a-z0-9._-]*", item) for item in extra):
        raise ValueError("explicit legacy repository inventory required")
    allowed.update(extra)
    protected = set()
    for reference in sorted(protected_references(root, config)):
        rows = strict_json(command(["docker", "image", "inspect", reference]))
        if len(rows) != 1 or reference not in rows[0].get("RepoDigests", []):
            raise ValueError("required rollback image absent or mismatched")
        protected.add(rows[0]["Id"])
    containers = command(["docker", "ps", "-aq"]).decode().split()
    for identifier in containers:
        protected.add(strict_json(command(["docker", "inspect", identifier]))[0]["Image"])
    identifiers = sorted(set(command(["docker", "image", "ls", "-aq", "--no-trunc"]).decode().split()))
    images = []
    for identifier in identifiers:
        row = strict_json(command(["docker", "image", "inspect", identifier]))[0]
        aliases = sorted(set(row.get("RepoTags") or []) | set(row.get("RepoDigests") or []))
        created = datetime.datetime.fromisoformat(row["Created"].replace("Z", "+00:00")).timestamp()
        # Untagged or mixed-ownership images are ambiguous, so preserve them.
        if identifier in protected or not aliases or any(repository(value) not in allowed for value in aliases) or created > now - 7 * 86400:
            continue
        images.append({"id": identifier, "references": aliases, "size_bytes": row["Size"]})
    archives = []
    backup_root = Path(config.get("backup_directory", str(root / "backups")))
    if not backup_root.is_absolute() or backup_root.is_symlink():
        raise ValueError("ordinary absolute backup directory required")
    for item in config.get("local_archives", []):
        path = Path(item["path"])
        if not path.is_absolute() or path.parent.resolve() != backup_root.resolve() or path.name != item["key"]:
            raise ValueError("explicit matching local archive path/key required")
        metadata = path.lstat()
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != os.geteuid() or metadata.st_mode & 0o077:
            raise ValueError("private ordinary local archive required")
        if metadata.st_mtime > now - 7 * 86400:
            continue
        if not re.fullmatch(r"[0-9a-f]{64}", item["sha256"]) or not item.get("version_id") or item["version_id"] == "null":
            raise ValueError("verified versioned archive receipt required")
        if metadata.st_size != item["size_bytes"] or digest(path) != item["sha256"]:
            raise ValueError("local archive does not match approved offsite receipt")
        archives.append({**item, "mtime_ns": metadata.st_mtime_ns, "inode": metadata.st_ino})
    result = {"images": images, "local_archives": archives,
              "protected_image_ids": sorted(protected),
              "note": "Image sizes overlap shared layers; measure actual filesystem after cleanup."}
    result["plan_sha256"] = hashlib.sha256(json.dumps(result, sort_keys=True).encode()).hexdigest()
    return result


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(block)
    return checksum.hexdigest()


def verify_offsite(item, environment):
    base = ["aws", "--endpoint-url", environment.get("BACKUP_S3_ENDPOINT", "https://storage.yandexcloud.net"), "s3api"]
    arguments = ["--bucket", item["bucket"], "--key", item["key"], "--version-id", item["version_id"]]
    env = {**os.environ, "AWS_ACCESS_KEY_ID": environment["BACKUP_S3_ACCESS_KEY"],
           "AWS_SECRET_ACCESS_KEY": environment["BACKUP_S3_SECRET_KEY"], "AWS_DEFAULT_REGION": "ru-central1"}
    head = strict_json(command(base + ["head-object", *arguments], env=env))
    if head.get("ContentLength") != item["size_bytes"] or head.get("Metadata", {}).get("sha256") != item["sha256"]:
        raise ValueError("offsite archive metadata mismatch")
    with tempfile.TemporaryDirectory(prefix="retention-readback-") as directory:
        path = Path(directory) / "archive"
        response = strict_json(command(base + ["get-object", *arguments, str(path)], env=env))
        if response.get("VersionId") != item["version_id"] or digest(path) != item["sha256"]:
            raise ValueError("offsite archive full read-back failed")


def apply(root, config, accepted_hash):
    plan = inventory(root, config)
    if plan["plan_sha256"] != accepted_hash:
        raise ValueError("inventory changed; review a new dry-run plan")
    if plan["local_archives"]:
        from environment_file import compose_environment
        environment = compose_environment(root / ".env", root)
        # Verify every offsite copy before deleting anything.
        for item in plan["local_archives"]:
            verify_offsite(item, environment)
    for image in plan["images"]:
        # No force: Docker also refuses references newly used by a container.
        command(["docker", "image", "rm", *image["references"]])
    for item in plan["local_archives"]:
        path = Path(item["path"])
        metadata = path.lstat()
        if path.is_symlink() or metadata.st_ino != item["inode"] or metadata.st_mtime_ns != item["mtime_ns"] or digest(path) != item["sha256"]:
            raise ValueError("local archive changed during cleanup")
        path.unlink()
    return plan


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/opt/education-platform"))
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--plan-sha256")
    args = parser.parse_args()
    os.umask(0o077)
    try:
        config = private_json(args.config)
        with (args.root / ".github-production/operation.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            if args.apply:
                if not args.plan_sha256:
                    raise ValueError("explicit accepted dry-run hash required")
                result = apply(args.root, config, args.plan_sha256)
            else:
                result = inventory(args.root, config)
        print(json.dumps({"mode": "apply" if args.apply else "dry-run", **result}, sort_keys=True))
    except (OSError, ValueError, RuntimeError, KeyError, TypeError, subprocess.SubprocessError):
        raise SystemExit("Retention refused; preserve existing files and review private inputs")


if __name__ == "__main__":
    main()
