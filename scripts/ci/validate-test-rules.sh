#!/usr/bin/env bash
# Validate GitHub backend selection against every service's csproj dependency graph.
# The optional integration matrix must name all thirteen services exactly once.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

exec python3 - "$REPO_ROOT" <<'PY'
import re
import sys
import pathlib
import json
import importlib.util

ROOT = pathlib.Path(sys.argv[1])

SERVICES = (
    "AccessService",
    "AssignmentReviewService",
    "AuthService",
    "CommentService",
    "EducationContentService",
    "FileService",
    "MaterialProcessingService",
    "NotificationService",
    "ProgressService",
    "SearchService",
    "TagService",
    "TelegramBotService",
    "TrainerService",
)


def derive_expected_globs(svc: str) -> set[str]:
    """Walk every csproj under backend/<svc>/ and derive required backend selection paths."""
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


def main() -> int:
    errors: list[str] = []
    module_spec = importlib.util.spec_from_file_location("github_ci", ROOT / "scripts/ci/github-ci.py")
    selector = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(selector)
    config = json.loads((ROOT / "scripts/ci/github-ci-paths.json").read_text())
    backend_paths = config["mandatory"]["backend"]
    integration = (ROOT / ".github/workflows/integration.yml").read_text()
    declared_services = re.search(r"^        service: \[([^\]]+)\]$", integration, re.MULTILINE)
    matrix = declared_services.group(1).replace(" ", "").split(",") if declared_services else []
    if len(matrix) != len(SERVICES) or set(matrix) != set(SERVICES):
        errors.append("GitHub on-demand integration matrix must contain all 13 services exactly")
    for required in ("global.json", ".gitmodules", "backend/Directory.Packages.props", "backend/Directory.Build.props", "backend/backend.slnx", "backend/nuget.config.ci"):
        if not any(selector.matches(required, glob) for glob in backend_paths):
            errors.append(f"GitHub backend checks do not select {required}")
    for svc in SERVICES:
        expected = derive_expected_globs(svc)
        for glob in expected:
            sample = glob.replace("/**/*", "/selection-probe.cs")
            if not any(selector.matches(sample, path) for path in backend_paths):
                errors.append(f"GitHub backend selection omits {svc} dependency {glob}")

    if errors:
        sys.stderr.write(
            "ERROR: integration-tests CI rules drift detected — "
            "csproj deps are not reflected in actual GitHub selection\n\n"
        )
        for e in errors:
            sys.stderr.write(e + "\n\n")
        sys.stderr.write(
            "Fix: update scripts/ci/github-ci-paths.json and the "
            "GitHub integration matrix.\n"
        )
        return 1

    print("OK: GitHub backend selection, all13 integration matrix match csproj deps.")
    return 0


sys.exit(main())
PY
