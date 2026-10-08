#!/usr/bin/env python3
"""Validate the repository instruction hierarchy and its context budgets."""

from __future__ import annotations

import argparse
from fnmatch import fnmatchcase
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote


ROOT_MAX_BYTES = 6_144
CHAIN_MAX_BYTES = 32_768
CLAUDE_MAX_BYTES = 1_024
IGNORED_PARTS = {
    ".git",
    ".harness",
    ".inside-harness",
    ".worktrees",
    "node_modules",
    "bin",
    "obj",
}
LINK_PATTERN = re.compile(r"(?<!!)\[[^\]]+\]\(([^)]+)\)")
FENCED_CODE_PATTERN = re.compile(r"```.*?```", re.DOTALL)
DUPLICATE_RULE_PAIRS = (
    ("docs/agents/backend-migrations.md", "docs/agents/migrations.md"),
    ("docs/agents/local-runtime.md", "docs/agents/platform-runtime.md"),
    ("docs/agents/doc-maintenance.md", "docs/agents/project-doc-maintenance.md"),
)
LEGACY_RUNTIME_PATHS = (
    ".claude/agents",
    ".claude/commands",
    ".claude/scripts/render-report.py",
    ".claude/settings.local.json.example",
    ".codex/agents",
)
REQUIRED_WORKFLOW_HEADINGS = (
    "## Review closure",
    "## Architecture fitness",
    "## Pruning",
)
REQUIRED_HARNESS_CI_PATTERNS = {
    ".agents/**/*",
    ".claude/**/*",
    ".codex/**/*",
    ".harness/**/*",
    ".inside-harness/**/*",
    ".templates/**/*",
    "**/AGENTS.md",
    "**/CLAUDE.md",
}
# Trunk-based contract: instructions must not route work through a second long-lived branch.
FORBIDDEN_BRANCH_PATTERNS = (
    re.compile(r"origin/dev\b"),
    re.compile(r"--target-branch[= ]dev\b"),
    re.compile(r"\bbackmerge", re.IGNORECASE),
    re.compile(r"\bdev\s*(?:→|->|-to-)\s*main\b", re.IGNORECASE),
    re.compile(
        r"(?:\b(?:into|onto|from|to|target|targeting|branch)|\s(?:в|из|от|ветк[аиуе]))\s+`?dev`?(?![\w.-])",
        re.IGNORECASE,
    ),
)
# Routed operational docs outside docs/agents that the branch contract also covers.
BRANCH_CONTRACT_DOCS = ("docs/ops.md", "docs/RUNBOOK.md")

def is_ignored(relative: Path) -> bool:
    return any(
        part in IGNORED_PARTS or part.startswith(".agent-template-contract-")
        for part in relative.parts
    )


def discover(root: Path, filename: str) -> list[Path]:
    return sorted(
        path
        for path in root.rglob(filename)
        if not is_ignored(path.relative_to(root))
        and not is_inside_nested_repository(root, path)
    )


def is_inside_nested_repository(root: Path, path: Path) -> bool:
    current = path.parent
    while current != root:
        if (current / ".git").exists():
            return True
        current = current.parent
    return False


def instruction_chain(root: Path, leaf: Path) -> list[Path]:
    chain: list[Path] = []
    current = root
    if (current / "AGENTS.md").is_file():
        chain.append(current / "AGENTS.md")

    for part in leaf.parent.relative_to(root).parts:
        current /= part
        candidate = current / "AGENTS.md"
        if candidate.is_file():
            chain.append(candidate)

    return chain


def local_link_target(source: Path, raw_target: str) -> Path | None:
    target = raw_target.strip().split(maxsplit=1)[0].strip("<>")
    if not target or target.startswith(("#", "/", "http://", "https://", "mailto:")):
        return None

    path_part = unquote(target.split("#", maxsplit=1)[0])
    if not path_part:
        return None
    return (source.parent / path_part).resolve()


def validate_symlink(root: Path, name: str, expected: str) -> list[str]:
    path = root / name
    if not path.is_symlink():
        return [f"{name} must be a symlink to {expected}"]
    actual = os.readlink(path)
    if actual != expected:
        return [f"{name} points to {actual}, expected {expected}"]
    if not path.resolve().exists():
        return [f"{name} is a broken symlink"]
    return []


def managed_skills_digest(snapshot: Path, skill_names: list[str]) -> str:
    """Hash deterministic path, executable mode, and content records for managed skill files."""
    digest = hashlib.sha256()
    files: list[Path] = []
    for skill_name in sorted(skill_names):
        skill_root = snapshot / skill_name
        files.extend(path for path in skill_root.rglob("*") if path.is_file())
    for path in sorted(files):
        relative = path.relative_to(snapshot).as_posix().encode("utf-8")
        # Git preserves only the executable bit. Checkout umasks may materialize the same
        # blob as 0644 or 0666, so normalize permissions to Git's two portable file modes.
        mode = b"755" if path.stat().st_mode & 0o111 else b"644"
        content = path.read_bytes()
        for record in (relative, mode, content):
            digest.update(len(record).to_bytes(8, byteorder="big"))
            digest.update(record)
    return digest.hexdigest()


def adapted_paths_digest(root: Path, path_names: list[str]) -> str:
    """Hash deterministic path, executable mode, and content for project-owned harness paths."""
    files: list[Path] = []
    for path_name in sorted(path_names):
        path = root / path_name
        if path.is_file():
            files.append(path)
        elif path.is_dir():
            files.extend(sorted(candidate for candidate in path.rglob("*") if candidate.is_file()))

    digest = hashlib.sha256()
    for path in sorted(set(files)):
        relative = path.relative_to(root).as_posix().encode("utf-8")
        mode = b"755" if path.stat().st_mode & 0o111 else b"644"
        content = path.read_bytes()
        for record in (relative, mode, content):
            digest.update(len(record).to_bytes(8, byteorder="big"))
            digest.update(record)
    return digest.hexdigest()


def validate_digest(
    *, label: str, digest_state: object, actual_digest: str
) -> list[str]:
    if not isinstance(digest_state, dict):
        return [f"{label} must be an object"]
    if digest_state.get("algorithm") != "sha256-path-mode-content-v1":
        return [f"{label} uses an unsupported algorithm"]
    expected_digest = str(digest_state.get("value", ""))
    if not re.fullmatch(r"[0-9a-f]{64}", expected_digest):
        return [f"{label}.value must be a SHA-256 digest"]
    if actual_digest != expected_digest:
        return [f"{label} mismatch: {actual_digest} != {expected_digest}"]
    return []


def ci_pattern_covers(path_name: str, pattern: str) -> bool:
    if pattern.endswith("/**/*"):
        root = pattern.removesuffix("/**/*")
        return path_name == root or path_name.startswith(root + "/")
    if pattern == "**/AGENTS.md":
        return path_name == "AGENTS.md" or path_name.endswith("/AGENTS.md")
    if pattern == "**/CLAUDE.md":
        return path_name == "CLAUDE.md" or path_name.endswith("/CLAUDE.md")
    return fnmatchcase(path_name, pattern)


def validate_gitlab_harness_triggers(root: Path, adapted_files: list[str]) -> list[str]:
    ci_path = root / ".gitlab-ci.yml"
    if not ci_path.is_file():
        return ["missing .gitlab-ci.yml for harness trigger validation"]
    ci = ci_path.read_text(encoding="utf-8")
    match = re.search(
        r"^validate-agent-skills:\n((?:[ \t].*\n|[ \t]*#.*\n|\s*\n)*)",
        ci,
        re.MULTILINE,
    )
    if not match:
        return ["cannot isolate validate-agent-skills CI job"]
    patterns = {
        item.strip().strip("'\"")
        for item in re.findall(r"^ {8}- ([^\n]+)$", match.group(1), re.MULTILINE)
    }
    errors: list[str] = []
    missing_patterns = sorted(REQUIRED_HARNESS_CI_PATTERNS - patterns)
    if missing_patterns:
        errors.append(
            "validate-agent-skills misses required trigger categories: "
            + ", ".join(missing_patterns)
        )
    for path_name in sorted(adapted_files):
        if not any(ci_pattern_covers(path_name, pattern) for pattern in patterns):
            errors.append(f"validate-agent-skills does not cover harness path: {path_name}")
    return errors


def validate_tracker_authority(root: Path) -> list[str]:
    path = root / "docs/agents/tracker-authority.json"
    try:
        authority = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return [f"invalid tracker authority: {error}"]
    if not isinstance(authority, dict):
        return ["tracker authority must be an object"]
    errors: list[str] = []
    for key, expected in {
        "gitlab_project": "miracle-generation/education-platform",
        "github_tracker": "KirillSachkov/education-platform-internal",
        "github_source": "KirillSachkov/education-platform",
    }.items():
        if authority.get(key) != expected:
            errors.append(f"tracker authority {key} must be {expected}")
    provider = authority.get("active_provider")
    activation = authority.get("activation")
    if provider == "gitlab":
        if activation is not None:
            errors.append("GitLab authority must have null activation")
    elif provider == "github":
        # Activation records initial source/CI, import and archive/restore evidence.
        # Production trials and retirement happen after activation; neither is a prerequisite.
        fields = ("owner_command", "import_receipt", "checks_receipt", "archive_receipt", "activated_at")
        if not isinstance(activation, dict) or any(
            not isinstance(activation.get(key), str) or not activation[key].strip()
            for key in fields
        ):
            errors.append("GitHub activation requires owner command, import/check/initial archive-restore receipts and UTC time")
        elif not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", activation["activated_at"]):
            errors.append("GitHub activated_at must be an ISO UTC timestamp")
    else:
        errors.append("active_provider must select exactly one of gitlab or github")
    return errors


def validate_ci_harness_triggers(root: Path, adapted_files: list[str]) -> list[str]:
    """Check the active CI plus any prepared GitHub contracts without requiring activation."""
    authority = json.loads((root / "docs/agents/tracker-authority.json").read_text())
    errors = validate_gitlab_harness_triggers(root, adapted_files) if authority.get("active_provider") == "gitlab" else []
    workflow = root / ".github/workflows/ci.yml"
    paths = root / "scripts/ci/github-ci-paths.json"
    if not workflow.exists() and not paths.exists() and authority.get("active_provider") != "github":
        return errors
    if not workflow.is_file() or not paths.is_file():
        return [*errors, "GitHub harness validation requires ci.yml and github-ci-paths.json"]
    try:
        config = json.loads(paths.read_text(encoding="utf-8"))
        patterns = config["mandatory"]["harness"]
        if not isinstance(patterns, list) or not patterns or not all(isinstance(p, str) for p in patterns):
            raise ValueError("mandatory.harness must be a nonempty string list")
    except (OSError, ValueError, KeyError, TypeError) as error:
        return [*errors, f"invalid GitHub harness path contract: {error}"]
    missing = sorted(REQUIRED_HARNESS_CI_PATTERNS - set(patterns))
    if missing:
        errors.append("GitHub harness misses required trigger categories: " + ", ".join(missing))
    for path_name in sorted(adapted_files):
        if not any(ci_pattern_covers(path_name, pattern) for pattern in patterns):
            errors.append(f"GitHub harness does not cover harness path: {path_name}")
    ci = workflow.read_text(encoding="utf-8")
    aggregate = re.search(r"^  required-checks:\n(.*?)(?=^  [\w-]+:|\Z)", ci, re.MULTILINE | re.DOTALL)
    if not aggregate or not re.search(r"^    name: required-checks\s*$", aggregate.group(1), re.MULTILINE):
        errors.append("GitHub aggregate must expose the required-checks job name")
    for doc_name in ("docs/agents/testing-profile.md", "docs/agents/release-pipelines.md"):
        doc = root / doc_name
        if not doc.is_file() or "`required-checks`" not in doc.read_text(encoding="utf-8"):
            errors.append(f"{doc_name} must name the actual GitHub required-checks aggregate")
    harness = re.search(r"^  harness:\n(.*?)(?=^  [\w-]+:|\Z)", ci, re.MULTILINE | re.DOTALL)
    selector = re.search(r"^  select:\n(.*?)(?=^  [\w-]+:|\Z)", ci, re.MULTILINE | re.DOTALL)
    if not selector or "python3 scripts/ci/github-ci.py select" not in selector.group(1):
        errors.append("GitHub select job must use the actual github-ci.py selector")
    if not (root / "scripts/ci/github-ci.py").is_file():
        errors.append("missing GitHub selector implementation")
    if not harness or any(token not in harness.group(1) for token in (
        "needs: select", "if: needs.select.outputs.harness == 'true'",
        "python3 scripts/ci/validate-agent-instructions.py",
    )):
        errors.append("GitHub harness job must run instruction validation using harness selection")
    return errors


def validate_harness(root: Path) -> list[str]:
    errors: list[str] = []
    state_path = root / ".inside-harness/product-harness.json"
    if not state_path.is_file():
        return ["missing .inside-harness/product-harness.json"]

    try:
        state = json.loads(state_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as error:
        return [f"invalid harness state: {error}"]

    if state.get("schemaVersion") != 2:
        errors.append("harness schemaVersion must be 2")
    if state.get("package") != "inside-engineering":
        errors.append("harness package must be inside-engineering")
    if not re.fullmatch(r"\d+\.\d+\.\d+", str(state.get("version", ""))):
        errors.append("harness version must be SemVer")
    source = state.get("source", {})
    if not source.get("repository") or not source.get("ref"):
        errors.append("harness source must include repository and ref")
    if not re.fullmatch(r"[0-9a-f]{40}", str(source.get("commit", ""))):
        errors.append("harness source.commit must be a full Git SHA")
    if state.get("managedFiles") != []:
        errors.append("managedFiles must be empty; Education adaptations are repository-owned")
    adapted_files = state.get("adaptedFiles")
    if not isinstance(adapted_files, list) or not all(
        isinstance(item, str) for item in adapted_files
    ):
        errors.append("adaptedFiles must be a string list")
        adapted_files = []
    if adapted_files != sorted(set(adapted_files)):
        errors.append("adaptedFiles must be sorted and unique")
    for adapted_name in adapted_files:
        if not (root / adapted_name).exists():
            errors.append(f"missing adapted harness path: {adapted_name}")

    managed = state.get("managedSkills")
    local = state.get("localSkills")
    if not isinstance(managed, list) or not all(isinstance(item, str) for item in managed):
        errors.append("managedSkills must be a string list")
        managed = []
    if not isinstance(local, list) or not all(isinstance(item, str) for item in local):
        errors.append("localSkills must be a string list")
        local = []
    if managed != sorted(set(managed)):
        errors.append("managedSkills must be sorted and unique")
    if local != sorted(set(local)):
        errors.append("localSkills must be sorted and unique")
    overlap = sorted(set(managed) & set(local))
    if overlap:
        errors.append(f"managedSkills and localSkills overlap: {', '.join(overlap)}")

    snapshot_name = state.get("snapshot")
    if snapshot_name != ".inside-harness/skills":
        errors.append("harness snapshot must be .inside-harness/skills")
    snapshot = root / ".inside-harness/skills"
    if not snapshot.is_dir():
        return [*errors, "missing .inside-harness/skills snapshot"]

    try:
        ignored = subprocess.run(
            ["git", "-C", str(root), "ls-files", "--others", "--ignored", "--exclude-standard",
             "--", ".inside-harness"],
            capture_output=True, text=True, check=False,
        )
    except FileNotFoundError:
        ignored = None
        errors.append("git is required to verify that the harness snapshot is tracked")
    if ignored is not None and ignored.returncode != 0:
        errors.append(f"cannot list ignored harness files: {ignored.stderr.strip()}")
    elif ignored is not None:
        ignored_files = [
            name for name in ignored.stdout.split() if Path(name).name != ".DS_Store"
        ]
        if ignored_files:
            errors.append(
                "git ignores harness snapshot files, so they never reach CI: "
                + ", ".join(ignored_files)
            )

    actual = sorted(path.name for path in snapshot.iterdir() if path.is_dir())
    declared = sorted([*managed, *local])
    if actual != declared:
        errors.append(
            "skill inventory differs from harness state: "
            f"actual={','.join(actual)} declared={','.join(declared)}"
        )

    errors.extend(
        validate_digest(
            label="managedDigest",
            digest_state=state.get("managedDigest"),
            actual_digest=managed_skills_digest(snapshot, managed),
        )
    )
    errors.extend(
        validate_digest(
            label="localDigest",
            digest_state=state.get("localDigest"),
            actual_digest=managed_skills_digest(snapshot, local),
        )
    )
    errors.extend(
        validate_digest(
            label="registryDigest",
            digest_state=state.get("registryDigest"),
            actual_digest=adapted_paths_digest(
                root, [".inside-harness/skills/REGISTRY.md"]
            ),
        )
    )
    errors.extend(
        validate_digest(
            label="adaptedDigest",
            digest_state=state.get("adaptedDigest"),
            actual_digest=adapted_paths_digest(root, adapted_files),
        )
    )
    if "integrationInventory" in state:
        errors.append("integrationInventory is not part of the main harness contract")
    authority_errors = validate_tracker_authority(root)
    errors.extend(authority_errors)
    if not authority_errors:
        errors.extend(validate_ci_harness_triggers(root, adapted_files))

    lifecycle = state.get("lifecycle")
    if not isinstance(lifecycle, dict):
        errors.append("harness lifecycle must be an object")
    else:
        if lifecycle.get("health") != (
            "python3 scripts/ci/validate-agent-instructions.py && "
            "bash scripts/ci/test-agent-template-contract.sh"
        ):
            errors.append("harness lifecycle health command drifted")
        if not lifecycle.get("updateManagedSnapshot"):
            errors.append("harness lifecycle must document managed snapshot updates")

    registry_path = snapshot / "REGISTRY.md"
    registry = registry_path.read_text(encoding="utf-8") if registry_path.is_file() else ""
    if not registry:
        errors.append("missing .inside-harness/skills/REGISTRY.md")
    registry_skills = set(
        re.findall(
            r"^\| `([^`]+)` \| `\.inside-harness/skills/[^`]+` \|",
            registry,
            re.MULTILINE,
        )
    )
    if registry_skills != set(actual):
        errors.append(
            "registry inventory differs from snapshot: "
            f"registry={','.join(sorted(registry_skills))} actual={','.join(actual)}"
        )

    for skill_name in actual:
        skill_file = snapshot / skill_name / "SKILL.md"
        if not skill_file.is_file():
            errors.append(f"missing SKILL.md for {skill_name}")
            continue
        content = skill_file.read_text(encoding="utf-8")
        if not content.startswith("---\n") or "\n---\n" not in content[4:]:
            errors.append(f"invalid frontmatter in {skill_file.relative_to(root)}")
            continue
        frontmatter = content.split("\n---\n", maxsplit=1)[0][4:]
        name_match = re.search(r"^name:\s*['\"]?([^'\"\n]+)['\"]?\s*$", frontmatter, re.MULTILINE)
        description_match = re.search(r"^description:\s*(\S.*)$", frontmatter, re.MULTILINE)
        declared_name = name_match.group(1).strip() if name_match else ""
        if declared_name != skill_name:
            errors.append(
                f"skill name mismatch in {skill_file.relative_to(root)}: {declared_name!r}"
            )
        if not description_match:
            errors.append(f"missing skill description in {skill_file.relative_to(root)}")
        registry_row = f"| `{skill_name}` | `.inside-harness/skills/{skill_name}` |"
        if registry_row not in registry:
            errors.append(f"missing registry row for {skill_name}")
        for markdown_file in sorted((snapshot / skill_name).rglob("*.md")):
            unfenced = FENCED_CODE_PATTERN.sub(
                "", markdown_file.read_text(encoding="utf-8")
            )
            for match in LINK_PATTERN.finditer(unfenced):
                target = local_link_target(markdown_file, match.group(1))
                if target is not None and not target.exists():
                    errors.append(
                        f"broken local link in {markdown_file.relative_to(root)}: "
                        f"{match.group(1)}"
                    )

    expected_targets = {
        "portable": ".agents/skills",
        "claude": ".claude/skills",
    }
    if state.get("targets") != expected_targets:
        errors.append("harness targets differ from runtime discovery paths")
    expected_compatibility = {
        "skills": ".harness/skills",
        "rules": ".harness/rules",
    }
    if state.get("compatibility") != expected_compatibility:
        errors.append("harness compatibility paths differ from repository links")

    for name, expected in (
        (".agents/skills", "../.inside-harness/skills"),
        (".claude/skills", "../.inside-harness/skills"),
        (".harness/skills", "../.inside-harness/skills"),
        (".harness/rules", "../docs/agents"),
    ):
        errors.extend(validate_symlink(root, name, expected))

    allowed_harness_entries = {"rules", "skills"}
    harness_dir = root / ".harness"
    actual_harness_entries = (
        {path.name for path in harness_dir.iterdir()} if harness_dir.is_dir() else set()
    )
    if actual_harness_entries != allowed_harness_entries:
        errors.append(
            ".harness may contain only compatibility links: "
            f"actual={','.join(sorted(actual_harness_entries))}"
        )

    return errors


def validate(root: Path, root_max: int, chain_max: int) -> list[str]:
    errors: list[str] = []
    agents_files = discover(root, "AGENTS.md")
    claude_files = discover(root, "CLAUDE.md")
    reference_files = sorted(
        path
        for path in root.rglob("*.md")
        if "docs/agent" in path.relative_to(root).as_posix()
        and not is_ignored(path.relative_to(root))
        and not is_inside_nested_repository(root, path)
    )
    workflow_files = [root / "WORKFLOW.md"] if (root / "WORKFLOW.md").is_file() else []
    root_agents = root / "AGENTS.md"

    if not root_agents.is_file():
        errors.append("missing root AGENTS.md")
        return errors

    for legacy_path in LEGACY_RUNTIME_PATHS:
        candidate = root / legacy_path
        if candidate.is_file() or (candidate.is_dir() and any(path.is_file() for path in candidate.rglob("*"))):
            errors.append(f"legacy runtime instruction layer must stay absent: {legacy_path}")
    legacy_scope_dirs = sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("agent")
        if path.is_dir()
        and path.name == "agent"
        and path.parent.name == "docs"
        and any(candidate.is_file() for candidate in path.rglob("*"))
        and not is_ignored(path.relative_to(root))
        and not is_inside_nested_repository(root, path)
    )
    if legacy_scope_dirs:
        errors.append(
            "legacy docs/agent scopes must stay absent: " + ", ".join(legacy_scope_dirs)
        )

    workflow = root / "WORKFLOW.md"
    workflow_content = workflow.read_text(encoding="utf-8") if workflow.is_file() else ""
    for heading in REQUIRED_WORKFLOW_HEADINGS:
        if heading not in workflow_content:
            errors.append(f"WORKFLOW.md is missing required heading: {heading}")
    if "--repo KirillSachkov/education-platform --base main" not in workflow_content:
        errors.append("WORKFLOW.md must require explicit public repository and --base main")
    if "--target-branch main" not in workflow_content:
        errors.append("WORKFLOW.md must require an explicit --target-branch main")

    tracker = root / "docs/agents/issue-tracker.md"
    tracker_content = tracker.read_text(encoding="utf-8") if tracker.is_file() else ""
    for required_tracker_section in ("## Shared-skill role mapping", "## Wayfinding operations"):
        if required_tracker_section not in tracker_content:
            errors.append(f"issue tracker contract is missing {required_tracker_section}")
    if not (root / "docs/agents/triage-labels.md").is_file():
        errors.append("missing docs/agents/triage-labels.md")

    root_size = root_agents.stat().st_size
    if root_size > root_max:
        errors.append(f"root AGENTS.md exceeds {root_max} bytes: {root_size}")

    for leaf in agents_files:
        chain = instruction_chain(root, leaf)
        chain_size = sum(path.stat().st_size for path in chain) + max(0, len(chain) - 1) * 2
        relative_leaf = leaf.relative_to(root)
        print(f"chain {relative_leaf}: {chain_size} bytes")
        if chain_size > chain_max:
            errors.append(
                f"instruction chain exceeds {chain_max} bytes at {relative_leaf}: {chain_size}"
            )

    for claude_file in claude_files:
        relative = claude_file.relative_to(root)
        local_agents = claude_file.with_name("AGENTS.md")
        content = claude_file.read_text(encoding="utf-8")
        if not local_agents.is_file():
            errors.append(f"{relative} has no local AGENTS.md")
        if content.count("@AGENTS.md") != 1:
            errors.append(f"{relative} must import @AGENTS.md exactly once")
        if claude_file.stat().st_size > CLAUDE_MAX_BYTES:
            errors.append(
                f"{relative} exceeds thin bridge budget {CLAUDE_MAX_BYTES} bytes: "
                f"{claude_file.stat().st_size}"
            )

    for template_name in ("platform-service", "vertical-slice-service"):
        template_root = root / ".templates" / template_name
        if not (template_root / "AGENTS.md").is_file():
            errors.append(f"template {template_name} must generate AGENTS.md")
        template_claude = template_root / "CLAUDE.md"
        if not template_claude.is_file() or template_claude.read_text(encoding="utf-8").strip() != "@AGENTS.md":
            errors.append(f"template {template_name} must generate a thin CLAUDE.md bridge")

    instruction_sources = {*agents_files, *claude_files, *workflow_files}
    for source in [*agents_files, *claude_files, *workflow_files, *reference_files]:
        content = FENCED_CODE_PATTERN.sub("", source.read_text(encoding="utf-8"))
        for match in LINK_PATTERN.finditer(content):
            target = local_link_target(source, match.group(1))
            if target is None or target.exists():
                continue
            target_suffix = Path(match.group(1).split("#", maxsplit=1)[0]).suffix
            if source not in instruction_sources and not target_suffix:
                continue
            if not target.exists():
                errors.append(
                    f"broken local link in {source.relative_to(root)}: {match.group(1)}"
                )

    local_skill_files: list[Path] = []
    state_path = root / ".inside-harness/product-harness.json"
    if state_path.is_file():
        try:
            local_skills = json.loads(state_path.read_text(encoding="utf-8")).get("localSkills", [])
        except (json.JSONDecodeError, OSError):
            local_skills = []
        for skill_name in local_skills if isinstance(local_skills, list) else []:
            skill_root = root / ".inside-harness/skills" / str(skill_name)
            local_skill_files.extend(sorted(skill_root.rglob("*.md")))
    contract_docs = [root / name for name in BRANCH_CONTRACT_DOCS if (root / name).is_file()]
    for source in sorted({*instruction_sources, *reference_files, *local_skill_files, *contract_docs}):
        content = source.read_text(encoding="utf-8")
        for pattern in FORBIDDEN_BRANCH_PATTERNS:
            match = pattern.search(content)
            if match:
                errors.append(
                    f"{source.relative_to(root)} routes work through a non-main branch: "
                    f"{match.group(0)!r}"
                )
                break

    for left_name, right_name in DUPLICATE_RULE_PAIRS:
        left = root / left_name
        right = root / right_name
        if left.exists() and right.exists():
            errors.append(f"forbidden duplicate rule pair: {left_name} + {right_name}")

    errors.extend(validate_harness(root))

    return errors


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--root-max", type=int, default=ROOT_MAX_BYTES)
    parser.add_argument("--chain-max", type=int, default=CHAIN_MAX_BYTES)
    parser.add_argument(
        "--write-digests",
        action="store_true",
        help="recompute product-harness.json digests from the working tree, then validate",
    )
    return parser.parse_args()


def write_digests(root: Path) -> None:
    state_path = root / ".inside-harness/product-harness.json"
    state = json.loads(state_path.read_text(encoding="utf-8"))
    snapshot = root / ".inside-harness/skills"
    state["managedDigest"]["value"] = managed_skills_digest(snapshot, state["managedSkills"])
    state["localDigest"]["value"] = managed_skills_digest(snapshot, state["localSkills"])
    state["registryDigest"]["value"] = adapted_paths_digest(
        root, [".inside-harness/skills/REGISTRY.md"]
    )
    state["adaptedDigest"]["value"] = adapted_paths_digest(root, state["adaptedFiles"])
    state_path.write_text(json.dumps(state, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    if args.write_digests:
        write_digests(root)
    errors = validate(root, args.root_max, args.chain_max)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("agent instruction contract passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
