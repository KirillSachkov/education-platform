#!/usr/bin/env python3
"""Record full-SHA image digests and require the complete application inventory."""

import argparse
import json
import os
from pathlib import Path
import re
import sys

REGISTRY = "ghcr.io/kirillsachkov/education-platform"
ROOT = Path(__file__).resolve().parents[2]


def image_record(name, digest, sha, names):
    if name not in names or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
        raise ValueError("unknown image or invalid content digest")
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or sha == "0" * 40:
        raise ValueError("full nonzero GitHub source SHA required")
    return {"name": name, "source_sha": sha, "tag": f"{REGISTRY}/{name}:{sha}",
            "digest": digest, "reference": f"{REGISTRY}/{name}@{digest}"}


def collect(records, sha, names):
    by_name = {}
    for record in records:
        canonical = image_record(record["name"], record["digest"], sha, names)
        if canonical != record or record["name"] in by_name:
            raise ValueError("stale, duplicate or inconsistent image record")
        by_name[record["name"]] = record
    if set(by_name) != names or len(names) != 14:
        raise ValueError("all 14 application images are required; no latest fallback")
    return {"schema_version": 1, "source_sha": sha, "images": [by_name[name] for name in sorted(names)]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["record", "collect"])
    args = parser.parse_args()
    names = {x["name"] for x in json.loads((ROOT / "scripts/ci/github-ci-paths.json").read_text())["images"]}
    sha = os.environ["GITHUB_SHA"]
    directory = Path("digests")
    if args.command == "record":
        record = image_record(os.environ["IMAGE_NAME"], os.environ["IMAGE_DIGEST"], sha, names)
        directory.mkdir(exist_ok=True)
        (directory / f"{record['name']}.json").write_text(json.dumps(record, indent=2) + "\n")
    else:
        records = [json.loads(p.read_text()) for p in directory.glob("*.json")]
        manifest = collect(records, sha, names)
        Path("image-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(summary, "a") as output:
                output.write(f"Source: `{sha}`. Complete image manifest; publication does not deploy.\n\n")
                output.write("| Image | Digest |\n| --- | --- |\n")
                output.writelines(f"| {x['name']} | `{x['digest']}` |\n" for x in manifest["images"])


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, OSError) as error:
        sys.exit(f"ERROR: {error}")
