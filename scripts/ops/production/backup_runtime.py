#!/usr/bin/env python3
"""Encrypt private runtime inputs and release records before an operation."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import time
import uuid

from environment_file import dotenv, private_environment


def snapshot(root, env_file):
    files = {"runtime.env": private_environment(env_file).encode(),
             "applied.env": private_environment(root / ".env").encode()}
    for name in ["releases/current.env", "releases/previous.env", "releases/pending.env",
                 ".github-production/current.json", ".github-production/previous.json",
                 ".github-production/pending.json", ".github-production/approved-legacy.json",
                 ".github-production/approved-config.json"]:
        path = root / name
        if path.exists():
            # Same root-private file contract, without interpreting JSON as dotenv.
            descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
            with os.fdopen(descriptor, "rb") as stream:
                before = os.fstat(stream.fileno())
                if before.st_uid != os.geteuid() or before.st_mode & 0o077:
                    raise ValueError("private recovery record permissions invalid")
                content = stream.read(16 * 1024 * 1024 + 1)
                after = os.fstat(stream.fileno())
                if len(content) > 16 * 1024 * 1024 or (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                    raise ValueError("private recovery record changed or exceeded limit")
            files[name] = content
    if "releases/current.env" not in files:
        raise ValueError("current release metadata required")
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w:gz") as archive:
        manifest = {}
        for name, data in files.items():
            manifest[name] = {"size": len(data), "sha256": hashlib.sha256(data).hexdigest()}
            member = tarfile.TarInfo(name); member.size = len(data); member.mode = 0o600
            archive.addfile(member, io.BytesIO(data))
        body = json.dumps(manifest).encode()
        member = tarfile.TarInfo("manifest.json"); member.size = len(body); member.mode = 0o600
        archive.addfile(member, io.BytesIO(body))
    return output.getvalue()


def command(arguments, *, env=None, input=None):
    result = subprocess.run(arguments, input=input, env=env, capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError("private runtime backup command failed")
    return result.stdout


def retain(root, env_file, recipient_file):
    values = dotenv(private_environment(env_file))
    recipient = recipient_file.read_text().strip()
    if not recipient.startswith("age1") or any(char.isspace() for char in recipient):
        raise ValueError("age public recipient required")
    for key in ["BACKUP_S3_ACCESS_KEY", "BACKUP_S3_SECRET_KEY", "BACKUP_S3_BUCKET"]:
        if not values.get(key): raise ValueError("runtime backup credentials missing")
    encrypted = command(["age", "-r", recipient], input=snapshot(root, env_file))
    checksum = hashlib.sha256(encrypted).hexdigest()
    environment = {**os.environ, "AWS_ACCESS_KEY_ID": values["BACKUP_S3_ACCESS_KEY"],
                   "AWS_SECRET_ACCESS_KEY": values["BACKUP_S3_SECRET_KEY"],
                   "AWS_DEFAULT_REGION": "ru-central1"}
    bucket = values["BACKUP_S3_BUCKET"]
    endpoint = values.get("BACKUP_S3_ENDPOINT", "https://storage.yandexcloud.net")
    base = ["aws", "--endpoint-url", endpoint, "s3api"]
    key = "runtime/" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + uuid.uuid4().hex + ".tar.gz.age"
    with tempfile.TemporaryDirectory(prefix="runtime-backup-") as directory:
        path = Path(directory) / "encrypted.age"
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as stream: stream.write(encrypted)
        upload = json.loads(command(base + ["put-object", "--bucket", bucket, "--key", key,
                                           "--body", str(path), "--acl", "private",
                                           "--metadata", "sha256=" + checksum], env=environment))
        version = upload.get("VersionId")
        if not isinstance(version, str) or not version or version == "null":
            raise ValueError("versioned private runtime backup required")
        head = json.loads(command(base + ["head-object", "--bucket", bucket, "--key", key,
                                         "--version-id", version], env=environment))
        if head.get("ContentLength") != len(encrypted) or head.get("Metadata", {}).get("sha256") != checksum:
            raise ValueError("runtime backup metadata mismatch")
        restored = Path(directory) / "readback.age"
        response = json.loads(command(base + ["get-object", "--bucket", bucket, "--key", key,
                                             "--version-id", version, str(restored)], env=environment))
        if response.get("VersionId") != version or hashlib.sha256(restored.read_bytes()).hexdigest() != checksum:
            raise ValueError("runtime backup read-back mismatch")
    return {"status": "PASS", "bucket": bucket, "key": key, "version_id": version,
            "sha256": checksum, "size_bytes": len(encrypted), "verified_at": time.time()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--env-file", required=True, type=Path)
    parser.add_argument("--recipient-file", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    os.umask(0o077)
    try:
        receipt = retain(args.root, args.env_file, args.recipient_file)
        args.receipt.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        with tempfile.NamedTemporaryFile(mode="w", dir=args.receipt.parent, delete=False) as stream:
            json.dump(receipt, stream); stream.flush(); os.fsync(stream.fileno())
        os.replace(stream.name, args.receipt)
        print("Private runtime backup and encrypted read-back passed.")
    except (OSError, ValueError, RuntimeError, KeyError, TypeError):
        # Provider errors and plaintext environment values never reach public logs.
        raise SystemExit("Private runtime backup failed; operation must stop")


if __name__ == "__main__":
    main()
