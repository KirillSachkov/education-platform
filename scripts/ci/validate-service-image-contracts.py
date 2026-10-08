#!/usr/bin/env python3
"""Validate service image ports and cross-service restore contexts from source."""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def tracked_service_projects() -> list[str]:
    projects = subprocess.run(
        ["git", "ls-files", "backend/*/src/*.Web/*.csproj"],
        cwd=ROOT, check=True, capture_output=True, text=True,
    ).stdout.splitlines()
    if not projects:
        fail("no tracked service web projects found")
    return sorted({Path(project).parts[1] for project in projects})


def validate_service_images(services: list[str]) -> None:
    for service in services:
        dockerfile = ROOT / "backend" / service / "Dockerfile"
        try:
            content = dockerfile.read_text(encoding="utf-8")
        except OSError as error:
            fail(f"cannot read {dockerfile.relative_to(ROOT)}: {error}")
        exposed_ports = re.findall(r"(?m)^EXPOSE\s+(\d+)\s*$", content)
        healthcheck_ports = re.findall(
            r'healthcheck\.dll"\s*,\s*"(\d+)"',
            content,
        )
        if len(exposed_ports) != 1:
            fail(f"{service} Dockerfile must expose exactly one numeric application port")
        if healthcheck_ports != exposed_ports:
            fail(
                f"{service} healthcheck port must equal EXPOSE port "
                f"(healthcheck={healthcheck_ports}, expose={exposed_ports})"
            )

        project_list = subprocess.run(
            ["git", "ls-files", f"backend/{service}/**/*.csproj"],
            cwd=ROOT,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.splitlines()
        for project_name in project_list:
            project = ROOT / project_name
            project_text = project.read_text(encoding="utf-8-sig")
            for reference in re.findall(r'<ProjectReference\s+Include="([^"]+)"', project_text):
                referenced = (project.parent / reference.replace("\\", "/")).resolve()
                try:
                    relative = referenced.relative_to(ROOT / "backend")
                except ValueError:
                    continue
                parts = relative.parts
                if (
                    len(parts) < 4
                    or parts[0] == service
                    or not parts[0].endswith("Service")
                    or not parts[-2].endswith(".Contracts")
                ):
                    continue
                project_path = relative.as_posix()
                source_directory = relative.parent.as_posix() + "/"
                if project_path not in content or source_directory not in content:
                    fail(
                        f"{service} Dockerfile omits cross-service contract context: "
                        f"{project_path}"
                    )


def validate_public_image_metadata(services: list[str]) -> None:
    dockerfiles = [ROOT / "backend" / service / "Dockerfile" for service in services]
    dockerfiles += [ROOT / "frontend/Dockerfile", *sorted((ROOT / ".templates").glob("*/Dockerfile"))]
    for dockerfile in dockerfiles:
        content = dockerfile.read_text()
        if 'org.opencontainers.image.source="https://github.com/KirillSachkov/education-platform"' not in content:
            fail(f"{dockerfile.relative_to(ROOT)} does not link the canonical public source")
        if dockerfile.parts[-3] != ".templates":
            if "id=image_notices" not in content or "dependency-notices/" not in content:
                fail(f"{dockerfile.relative_to(ROOT)} does not preserve image dependency notices")


def main() -> None:
    services = tracked_service_projects()
    validate_service_images(services)
    validate_public_image_metadata(services)
    print(f"Service image contracts passed: {len(services)} services")


if __name__ == "__main__":
    main()
