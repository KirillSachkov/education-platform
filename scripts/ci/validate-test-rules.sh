#!/usr/bin/env bash
# Drift-gate for actual GitHub selection and transitional GitLab suite rules.
#
# Walks every `backend/<Service>/**/*.csproj`, extracts `<ProjectReference>` items
# pointing at `Shared/*` libs and other `*Service.Contracts` projects, then checks
# that each derived glob is covered by the corresponding job's `changes:` list.
#
# Fails the MR (exit 1) if any service's csproj graph has a dependency that the
# CI rules do not include — this keeps the static dep-map honest as the project
# graph evolves.
#
# Runs in alpine via python3.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

exec python3 - "$REPO_ROOT" <<'PY'
import re
import sys
import pathlib
import json
import importlib.util

ROOT = pathlib.Path(sys.argv[1])

SVC_TO_JOB = {
    "AccessService": "access",
    "AssignmentReviewService": "assignment-review",
    "AuthService": "auth",
    "CommentService": "comment",
    "EducationContentService": "education",
    "FileService": "file",
    "MaterialProcessingService": "material-processing",
    "NotificationService": "notification",
    "ProgressService": "progress",
    "SearchService": "search",
    "TagService": "tag",
    "TelegramBotService": "telegram-bot",
    "TrainerService": "trainer",
}


def parse_ci_yaml(path: pathlib.Path) -> dict[str, set[str]]:
    """For each `integration-tests:<name>:` job, return its concrete `changes:` globs.

    Skips the `- changes: *ultra-paths` anchor reference — only the inline list is read.
    """
    lines = path.read_text().splitlines()
    out: dict[str, set[str]] = {}

    i = 0
    while i < len(lines):
        m = re.match(r"^integration-tests:([\w-]+):\s*$", lines[i])
        if not m:
            i += 1
            continue

        job = m.group(1)
        globs: set[str] = set()
        in_concrete_changes = False
        j = i + 1
        while j < len(lines):
            line = lines[j]
            # End of job block: a new top-level (column-0) key
            if line and not line.startswith(" ") and not line.startswith("\t"):
                break

            # New rule entry — leaving the changes list
            if re.match(r"^    - ", line):
                in_concrete_changes = False
                if re.match(r"^    - changes:\s*$", line):
                    in_concrete_changes = True
                # anchor refs like `- changes: *ultra-paths` keep in_concrete_changes=False

            if in_concrete_changes:
                gm = re.match(r"^        - (.+?)\s*$", line)
                if gm:
                    globs.add(gm.group(1))

            j += 1

        out[job] = globs
        i = j

    return out


def derive_expected_globs(svc: str) -> set[str]:
    """Walk every csproj under backend/<svc>/ and derive required `changes:` globs."""
    expected: set[str] = {f"backend/{svc}/**/*"}
    svc_dir = ROOT / "backend" / svc

    for csproj in svc_dir.rglob("*.csproj"):
        text = csproj.read_text(encoding="utf-8", errors="ignore")
        for ref in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text):
            ref_norm = ref.replace("\\", "/")

            # Shared/<dir>[/<sub>]
            sm = re.search(r"Shared/([^/]+)(?:/([^/]+))?", ref_norm)
            if sm:
                d1, d2 = sm.group(1), sm.group(2)
                if d2 is None or d2.endswith(".csproj"):
                    expected.add(f"backend/Shared/{d1}/**/*")
                else:
                    expected.add(f"backend/Shared/{d1}/{d2}/**/*")

            # Other-service Contracts
            cm = re.search(r"([A-Za-z]+Service)\.Contracts", ref_norm)
            if cm:
                other = cm.group(1)
                if other != svc:
                    expected.add(f"backend/{other}/src/{other}.Contracts/**/*")

    return expected


def covered(needle: str, haystack: set[str]) -> bool:
    """True if `needle` glob is satisfied by an equal or parent-dir glob in haystack."""
    if needle in haystack:
        return True
    if not needle.endswith("/**/*"):
        return False
    parts = needle[: -len("/**/*")].split("/")
    # Try every shorter parent path
    for k in range(len(parts) - 1, 0, -1):
        parent = "/".join(parts[:k]) + "/**/*"
        if parent in haystack:
            return True
    return False


def main() -> int:
    ci_yaml = ROOT / ".gitlab-ci.yml"
    if not ci_yaml.exists():
        print(f"ERROR: {ci_yaml} not found", file=sys.stderr)
        return 1

    job_rules = parse_ci_yaml(ci_yaml)

    errors: list[str] = []
    module_spec = importlib.util.spec_from_file_location("github_ci", ROOT / "scripts/ci/github-ci.py")
    selector = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(selector)
    config = json.loads((ROOT / "scripts/ci/github-ci-paths.json").read_text())
    backend_paths = config["mandatory"]["backend"]
    integration = (ROOT / ".github/workflows/integration.yml").read_text()
    declared_services = re.search(r"^        service: \[([^\]]+)\]$", integration, re.MULTILINE)
    if not declared_services or set(declared_services.group(1).replace(" ", "").split(",")) != set(SVC_TO_JOB):
        errors.append("GitHub on-demand integration matrix must contain all 13 services exactly")
    for required in ("global.json", ".gitmodules", "backend/Directory.Packages.props", "backend/Directory.Build.props", "backend/backend.slnx", "backend/nuget.config.ci"):
        if not any(selector.matches(required, glob) for glob in backend_paths):
            errors.append(f"GitHub backend checks do not select {required}")
    for svc, job in SVC_TO_JOB.items():
        if job not in job_rules:
            errors.append(f"integration-tests:{job} job missing from .gitlab-ci.yml")
            continue

        declared = job_rules[job]
        if not declared:
            errors.append(
                f"integration-tests:{job} has no concrete `changes:` list "
                f"— expected globs derived from backend/{svc} csproj refs"
            )
            continue

        expected = derive_expected_globs(svc)
        for glob in expected:
            sample = glob.replace("/**/*", "/selection-probe.cs")
            if not any(selector.matches(sample, path) for path in backend_paths):
                errors.append(f"GitHub backend selection omits {svc} dependency {glob}")
        missing = sorted(g for g in expected if not covered(g, declared))
        if missing:
            errors.append(
                f"integration-tests:{job} — `changes:` list is missing entries "
                f"derived from backend/{svc} csproj refs:\n"
                + "\n".join(f"    - {g}" for g in missing)
            )

    if errors:
        sys.stderr.write(
            "ERROR: integration-tests CI rules drift detected — "
            "csproj deps are not reflected in actual GitHub selection or transitional GitLab CI\n\n"
        )
        for e in errors:
            sys.stderr.write(e + "\n\n")
        sys.stderr.write(
            "Fix: add the missing globs to the relevant integration-tests:<svc> "
            "job's `changes:` list in .gitlab-ci.yml.\n"
        )
        return 1

    print("OK: GitHub backend selection, all13 integration matrix and transitional GitLab rules match csproj deps.")
    return 0


sys.exit(main())
PY
