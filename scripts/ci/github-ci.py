#!/usr/bin/env python3
"""Select mandatory GitHub checks and reject incomplete aggregate results."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "scripts/ci/github-ci-paths.json"
ZERO = "0" * 40


def git(*args, cwd=ROOT):
    return subprocess.check_output(["git", *args], cwd=cwd, stderr=subprocess.PIPE).decode().strip()


def commit(sha, cwd=ROOT):
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{40}", sha) or sha == ZERO:
        raise ValueError("expected a nonzero full commit SHA")
    if git("rev-parse", "--verify", f"{sha}^{{commit}}", cwd=cwd) != sha:
        raise ValueError("commit SHA does not resolve exactly")
    return sha


def matches(path, pattern):
    # GitLab-style paths: **/ also matches zero directories; * never crosses /.
    tokens = re.split(r"(\*\*/|\*\*|\*|\?)", pattern)
    regex = "".join({"**/": "(?:.*/)?", "**": ".*", "*": "[^/]*", "?": "[^/]"}.get(t, re.escape(t)) for t in tokens)
    return re.fullmatch(regex, path) is not None


def changed_paths(base, head, cwd=ROOT):
    # Disable rename detection so BOTH names select checks; keep deletions.
    raw = subprocess.check_output(["git", "diff", "--no-renames", "--name-only", "-z", base, head], cwd=cwd)
    return [p.decode() for p in raw.split(b"\0") if p]


def select(event_name, event, head, config, cwd=ROOT):
    head = commit(head, cwd)
    if event_name == "pull_request":
        pr = event["pull_request"]
        if pr["base"]["ref"] != "main":
            raise ValueError("PR must target main")
        base_tip = commit(pr["base"]["sha"], cwd)
        pr_head = commit(pr["head"]["sha"], cwd)
        base = commit(git("merge-base", base_tip, pr_head, cwd=cwd), cwd)
        # The workflow tests GitHub's synthetic merge commit. It must contain both
        # immutable event tips; selection compares merge-base with the PR head.
        for tip in (base_tip, pr_head):
            subprocess.run(["git", "merge-base", "--is-ancestor", tip, head], cwd=cwd, check=True)
        paths = changed_paths(base, pr_head, cwd)
        all_checks = False
    elif event_name == "push":
        if event["ref"] != "refs/heads/main" or event["after"] != head or event.get("deleted", False):
            raise ValueError("expected a live main push at the checked-out SHA")
        before = event["before"]
        if before == ZERO:
            # The approved public bootstrap has no parents. Empty tree diff also
            # gives formatting/migration checks a real base, without HEAD~1.
            if len(git("rev-list", "--parents", "-n", "1", head, cwd=cwd).split()) != 1:
                raise ValueError("zero-before bootstrap must have zero parents")
            base = subprocess.check_output(["git", "hash-object", "-w", "-t", "tree", "--stdin"], input=b"", cwd=cwd).decode().strip()
        else:
            base = commit(before, cwd)
            subprocess.run(["git", "merge-base", "--is-ancestor", base, head], cwd=cwd, check=True)
        paths = changed_paths(base, head, cwd)
        all_checks = True
    else:
        raise ValueError(f"unsupported selection event: {event_name}")
    selected = {key: all_checks or any(matches(p, g) for p in paths for g in globs) for key, globs in config["mandatory"].items()}
    return {"base": base, "head": head, "diff_head": pr_head if event_name == "pull_request" else head,
            "bootstrap": event_name == "push" and event["before"] == ZERO,
            "selected": selected, "images_matrix": {"include": config["images"]}, "paths": paths}


def aggregate(needs):
    selector = needs.get("select", {})
    if selector.get("result") != "success":
        raise ValueError("selection failed, was skipped or was cancelled")
    selected = json.loads(selector["outputs"]["selected"])
    expected = set(json.loads(CONFIG.read_text())["mandatory"])
    if set(selected) != expected or any(type(v) is not bool for v in selected.values()):
        raise ValueError("incomplete or invalid deterministic selection")
    expected_jobs = {"select", "whitespace", *expected}
    if set(needs) != expected_jobs:
        raise ValueError("aggregate needs must cover every mandatory job exactly")
    for job in sorted(expected_jobs - {"select"}):
        result = needs[job].get("result")
        required = job == "whitespace" or selected[job]
        if result != "success" and (required or result != "skipped"):
            raise ValueError(f"{job}: {result}; expected {'success' if required else 'success or intentionally skipped'}")
    print("required-checks: every selected mandatory job passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["select", "aggregate"])
    args = parser.parse_args()
    if args.command == "aggregate":
        aggregate(json.loads(os.environ["NEEDS_JSON"]))
        return
    result = select(os.environ["GITHUB_EVENT_NAME"], json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text()),
                    git("rev-parse", "HEAD"), json.loads(CONFIG.read_text()))
    with open(os.environ["GITHUB_OUTPUT"], "a") as output:
        for key in ("base", "head", "diff_head", "selected", "images_matrix"):
            value = result[key]
            output.write(f"{key}={json.dumps(value, separators=(',', ':')) if isinstance(value, dict) else value}\n")
        for key, value in result["selected"].items():
            output.write(f"{key}={str(value).lower()}\n")
    print(json.dumps(result, indent=2))
    if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(summary, "a") as output:
            output.write(f"Selection base: `{result['base']}`; head: `{result['head']}`; bootstrap: `{result['bootstrap']}`.\n\n")
            output.write("| Check | Selected |\n| --- | --- |\n")
            output.writelines(f"| {key} | {value} |\n" for key, value in result["selected"].items())


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(f"ERROR: {error}")
