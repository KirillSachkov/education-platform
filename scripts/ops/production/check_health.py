#!/usr/bin/env python3
"""Small, read-only runtime check. Alerts require an explicitly configured webhook."""

import argparse
import json
import os
from pathlib import Path
import stat
import subprocess
import time
import urllib.parse
import urllib.request


def private_json(path):
    metadata = path.lstat()
    if (not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != os.geteuid()
            or metadata.st_mode & 0o077):
        raise ValueError("private file ownership or permissions invalid")
    value = json.loads(path.read_text())
    if not isinstance(value, dict):
        raise ValueError("JSON object required")
    return value


def https_url(value):
    url = urllib.parse.urlsplit(value)
    if url.scheme != "https" or not url.hostname or url.username or url.password:
        raise ValueError("HTTPS endpoint without userinfo required")
    return value


def inspect_containers(names):
    # Capture output: docker inspect contains private environment values.
    result = subprocess.run(["docker", "inspect", *names], check=True,
                            capture_output=True, timeout=30)
    return json.loads(result.stdout)


def container_failures(containers, expected, project):
    failures = []
    actual = {item["Name"].lstrip("/"): item for item in containers}
    for name, service in expected.items():
        item = actual.get(name, {})
        labels = item.get("Config", {}).get("Labels", {})
        state = item.get("State", {})
        if (labels.get("com.docker.compose.project") != project
                or labels.get("com.docker.compose.service") != service
                or not state.get("Running")
                or (name not in {"redis-platform", "nginx"}
                    and state.get("Health", {}).get("Status") != "healthy")):
            failures.append("container:" + name)
    return failures


def backup_fresh(receipt, now, max_age=30 * 3600):
    verified = receipt.get("verified_at")
    return (type(verified) in (int, float) and 0 <= now - verified <= max_age
            and type(receipt.get("size_bytes")) is int and receipt["size_bytes"] >= 10240
            and isinstance(receipt.get("sha256"), str) and len(receipt["sha256"]) == 64)


def check(config, now):
    root = Path(config["root"])
    failures = []
    # Use the accepted release inventory; stopped historical containers are irrelevant.
    try:
        release = private_json(root / ".github-production/current.json")
        names = [row["name"] for row in release["image_manifest"]["images"]]
        if not names or any(not isinstance(name, str) or not name for name in names):
            raise ValueError("release inventory missing")
        expected = {name: name for name in names}
        expected.update({"postgres": "postgres", "rabbitmq": "rabbitmq",
                         "redis-platform": "redis", "nginx": "nginx"})
        failures.extend(container_failures(inspect_containers(list(expected)), expected, config["project"]))
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        failures.append("runtime-inventory")
    try:
        request = urllib.request.Request(https_url(config["public_url"]), method="GET")
        with urllib.request.urlopen(request, timeout=15) as response:
            if response.status != 200:
                failures.append("public-http")
    except (OSError, ValueError):
        failures.append("public-http")
    try:
        if not backup_fresh(private_json(Path(config["backup_receipt"])), now):
            failures.append("backup-stale")
    except (OSError, ValueError, TypeError):
        failures.append("backup-receipt")
    try:
        disk = os.statvfs(root)
        if disk.f_blocks <= 0 or disk.f_bavail / disk.f_blocks < 0.15:
            failures.append("disk-space")
    except OSError:
        failures.append("disk-space")
    return sorted(failures)


def send_alert(url, failures):
    # Compatible with a text webhook. Never include URLs, private files or inspect output.
    body = json.dumps({"text": "Education platform checks failed: " + ", ".join(failures)}).encode()
    request = urllib.request.Request(https_url(url), data=body,
                                     headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=15) as response:
        if response.status not in range(200, 300):
            raise ValueError("alert rejected")


def run(config, *, notify=False):
    now = time.time()
    failures = check(config, now)
    if failures and notify:
        try:
            send_alert(config["alert_webhook_url"], failures)
        except (OSError, ValueError, KeyError):
            failures.append("alert-delivery")
    print(json.dumps({"status": "FAIL" if failures else "PASS", "checks_failed": failures}))
    return int(bool(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--notify", action="store_true", help="send alerts to the configured webhook")
    args = parser.parse_args()
    try:
        config = private_json(args.config)
        raise SystemExit(run(config, notify=args.notify))
    except (OSError, ValueError, KeyError, TypeError):
        print('{"status":"FAIL","checks_failed":["monitor-configuration"]}')
        raise SystemExit(1)


if __name__ == "__main__":
    main()
