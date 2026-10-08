#!/usr/bin/env python3
"""Exempt byte-identical historical debt only for a verified public bootstrap."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PUBLIC_REPO = "KirillSachkov/education-platform"
ZERO = "0" * 40


def bootstrap(event_name, event, repository, base, head, root=ROOT):
    if event_name != "push":
        return False
    if event["before"] != ZERO:
        return False
    if repository != PUBLIC_REPO or event["repository"]["full_name"] != PUBLIC_REPO or event["ref"] != "refs/heads/main":
        raise ValueError("zero-before exemption requires trusted public main")
    actual_head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root).decode().strip()
    parents = subprocess.check_output(["git", "rev-list", "--parents", "-n", "1", actual_head], cwd=root).decode().split()
    empty_tree = subprocess.check_output(["git", "hash-object", "-t", "tree", "--stdin"], cwd=root, input=b"").decode().strip()
    if event["after"] != actual_head or head != actual_head or len(parents) != 1 or base != empty_tree:
        raise ValueError("bootstrap must use the real zero-parent commit and available empty-tree diff base")
    return True


def filter_debt(paths, baseline, root=ROOT):
    remaining = []
    exempt = 0
    for path in paths:
        current = root / path
        if path in baseline and hashlib.sha256(current.read_bytes()).hexdigest() == baseline[path]:
            exempt += 1
        else:
            remaining.append(path)
    return remaining, exempt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tool", choices=("prettier", "whitespace"), default="prettier")
    tool = parser.parse_args().tool
    paths = [p.decode() for p in sys.stdin.buffer.read().split(b"\0") if p]
    event_name = os.environ.get("GITHUB_EVENT_NAME", "")
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text()) if event_name == "push" else {}
    eligible = bootstrap(event_name, event, os.environ.get("GITHUB_REPOSITORY", ""),
                         os.environ["CI_DIFF_BASE"], os.environ.get("CI_DIFF_HEAD", "HEAD"))
    if eligible:
        baseline_file = ROOT / f"scripts/ci/github-bootstrap-{tool}-baseline.json"
        baseline = json.loads(baseline_file.read_text())["files"]
        paths, exempt = filter_debt(paths, baseline)
        message = f"Bootstrap {tool}: {exempt} byte-identical debt files; {len(paths)} inputs require strict formatting."
        print(message, file=sys.stderr)
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(summary, "a") as output:
                output.write(message + "\n")
    sys.stdout.buffer.write(b"".join(p.encode() + b"\0" for p in paths))


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(f"ERROR: {error}")
