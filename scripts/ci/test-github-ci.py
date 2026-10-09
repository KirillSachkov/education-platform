#!/usr/bin/env python3
"""Exercise GitHub event selection, required-check failure cases and image inventory."""

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import os
import shutil

ROOT = Path(__file__).resolve().parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts/ci" / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


ci = module("github_ci", "github-ci.py")
images = module("github_images", "github-image-manifest.py")
formatting = module("bootstrap_formatting", "github-bootstrap-formatting.py")
CONFIG = json.loads(ci.CONFIG.read_text())


class Selection(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name)
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "ci@example.invalid")
        self.git("config", "user.name", "CI fixture")
        self.write("README.md", "Initial source\n")
        self.initial = self.save()

    def git(self, *args):
        return ci.git(*args, cwd=self.repo)

    def write(self, path, value):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(value)

    def save(self):
        self.git("add", ".")
        self.git("commit", "-qm", "fixture")
        return self.git("rev-parse", "HEAD")

    def pr(self, head, base=None, checkout=None):
        event = {"pull_request": {"base": {"ref": "main", "sha": base or self.initial}, "head": {"sha": head}}}
        return ci.select("pull_request", event, checkout or head, CONFIG, self.repo)

    def test_zero_parent_bootstrap_selects_all_classes_and_images(self):
        event = {"ref": "refs/heads/main", "before": ci.ZERO, "after": self.initial}
        result = ci.select("push", event, self.initial, CONFIG, self.repo)
        self.assertTrue(all(result["selected"].values()))
        self.assertEqual(13, len(result["images_matrix"]["include"]))
        self.assertEqual("tree", self.git("cat-file", "-t", result["base"]))
        self.write("docs/new.md", "new")
        with self.assertRaises(ValueError):
            ci.select("push", {**event, "after": self.save()}, self.git("rev-parse", "HEAD"), CONFIG, self.repo)

    def test_precise_docs_and_frontend_selection(self):
        self.write("docs/contributor.md", "docs")
        self.assertFalse(any(self.pr(self.save())["selected"].values()))
        self.write("frontend/src/example.ts", "export {}")
        chosen = self.pr(self.save())["selected"]
        self.assertTrue(chosen["frontend"])
        self.assertFalse(chosen["backend"])

    def test_harness_root_and_nested_paths(self):
        self.assertTrue(ci.matches("AGENTS.md", "**/AGENTS.md"))
        for path in ("AGENTS.md", ".inside-harness/product-harness.json", "backend/AuthService/AGENTS.md", "docs/contributing-ci.md"):
            base = self.git("rev-parse", "HEAD")
            self.write(path, "instructions")
            self.assertTrue(self.pr(self.save(), base)["selected"]["harness"], path)

    def test_sdk_submodule_and_workflow_paths(self):
        for path in ("global.json", ".gitmodules", ".github/actions/setup-dotnet/action.yml"):
            selected = {k: any(ci.matches(path, g) for g in globs) for k, globs in CONFIG["mandatory"].items()}
            for key in ("backend", "templates", "images", "rules"):
                self.assertTrue(selected[key], (path, key))
        self.assertTrue(all(any(ci.matches(".github/workflows/ci.yml", g) for g in patterns) for patterns in CONFIG["mandatory"].values()))
        for path in (".templates/platform-service/Dockerfile", ".templates/vertical-slice-service/Dockerfile"):
            self.assertTrue(any(ci.matches(path, g) for g in CONFIG["mandatory"]["images"]))

    def test_rename_and_delete_select_old_and_new_paths(self):
        self.write("backend/AuthService/probe.cs", "backend")
        base = self.save()
        self.git("mv", "backend/AuthService/probe.cs", "README.cs")
        self.write("frontend/old.ts", "frontend")
        head = self.save()
        self.assertTrue(self.pr(head, base)["selected"]["backend"])
        (self.repo / "frontend/old.ts").unlink()
        self.assertTrue(self.pr(self.save(), head)["selected"]["frontend"])

    def test_event_merge_base_excludes_unrelated_base_tip(self):
        self.git("checkout", "-qb", "topic")
        self.write("frontend/change.ts", "frontend")
        pr_head = self.save()
        self.git("checkout", "-q", "main")
        self.write("backend/Change.cs", "main-only")
        base_tip = self.save()
        self.git("merge", "-q", "--no-ff", "topic", "-m", "synthetic merge")
        result = self.pr(pr_head, base_tip, self.git("rev-parse", "HEAD"))
        self.assertEqual(self.initial, result["base"])
        self.assertEqual(pr_head, result["diff_head"])
        self.assertFalse(result["selected"]["backend"])
        with self.assertRaises(subprocess.CalledProcessError):
            self.pr(pr_head, base_tip, pr_head)

    def test_missing_invalid_and_non_main_inputs_fail_closed(self):
        for bad in ("origin/dev", "bad", ci.ZERO, "f" * 40):
            with self.assertRaises((ValueError, subprocess.CalledProcessError)):
                self.pr(bad)
        with self.assertRaises(ValueError):
            ci.select("push", {"ref": "refs/heads/topic", "before": ci.ZERO, "after": self.initial}, self.initial, CONFIG, self.repo)

    def test_output_names_do_not_overwrite_image_matrix(self):
        directory = self.repo / "scripts/ci"
        directory.mkdir(parents=True)
        for filename in ("github-ci.py", "github-ci-paths.json"):
            shutil.copyfile(ROOT / "scripts/ci" / filename, directory / filename)
        event = self.repo / "event.json"
        event.write_text(json.dumps({"pull_request": {"base": {"ref": "main", "sha": self.initial}, "head": {"sha": self.initial}}}))
        output = self.repo / "output"
        subprocess.run(["python3", str(directory / "github-ci.py"), "select"], cwd=self.repo, check=True,
                       env={**os.environ, "GITHUB_EVENT_NAME": "pull_request", "GITHUB_EVENT_PATH": str(event), "GITHUB_OUTPUT": str(output)},
                       stdout=subprocess.DEVNULL)
        values = dict(line.split("=", 1) for line in output.read_text().splitlines())
        self.assertEqual("false", values["images"])
        self.assertEqual(13, len(json.loads(values["images_matrix"])["include"]))

    def test_bootstrap_exemption_is_guarded_and_content_exact(self):
        import hashlib
        directory = self.repo / "scripts/ci"
        directory.mkdir(parents=True)
        for filename in ("prettier-check-diff.sh", "github-bootstrap-formatting.py"):
            shutil.copyfile(ROOT / "scripts/ci" / filename, directory / filename)
        debt = "frontend/src/debt.ts"
        self.write(debt, "unformatted fixture\n")
        baseline = {debt: hashlib.sha256((self.repo / debt).read_bytes()).hexdigest()}
        (directory / "github-bootstrap-prettier-baseline.json").write_text(json.dumps({"files": baseline}))
        self.save()
        # Create a genuine zero-parent public source fixture, without any private
        # original commit id in the public tests or baseline provenance.
        tree = self.git("write-tree")
        root_head = subprocess.check_output(["git", "commit-tree", tree], cwd=self.repo, input=b"public bootstrap fixture\n").decode().strip()
        self.git("reset", "--hard", root_head)
        empty = subprocess.check_output(["git", "hash-object", "-w", "-t", "tree", "--stdin"], cwd=self.repo, input=b"").decode().strip()
        event = {"before": ci.ZERO, "after": root_head, "ref": "refs/heads/main", "repository": {"full_name": formatting.PUBLIC_REPO}}
        self.assertTrue(formatting.bootstrap("push", event, formatting.PUBLIC_REPO, empty, root_head, self.repo))
        self.assertFalse(formatting.bootstrap("pull_request", event, formatting.PUBLIC_REPO, empty, root_head, self.repo))
        self.assertFalse(formatting.bootstrap("push", {**event, "before": root_head}, formatting.PUBLIC_REPO, empty, root_head, self.repo))
        for repository in ("fork/example", ""):
            with self.assertRaises(ValueError):
                formatting.bootstrap("push", event, repository, empty, root_head, self.repo)
        event_file = self.repo / "event.json"
        event_file.write_text(json.dumps(event))
        binary = self.repo / "bin"
        binary.mkdir()
        formatter = binary / "npx"
        formatter.write_text('#!/bin/sh\nexit 1\n')
        formatter.chmod(0o755)
        env = {**os.environ, "PATH": str(binary) + os.pathsep + os.environ["PATH"],
               "CI_DIFF_BASE": empty, "CI_DIFF_HEAD": root_head, "GITHUB_EVENT_NAME": "push",
               "GITHUB_EVENT_PATH": str(event_file), "GITHUB_REPOSITORY": formatting.PUBLIC_REPO}
        def run(**changes):
            return subprocess.run(["bash", str(directory / "prettier-check-diff.sh")], cwd=self.repo,
                env={**env, **changes}, stdout=subprocess.PIPE, stderr=subprocess.PIPE).returncode
        self.assertEqual(0, run()) # Exact untouched debt is the sole exemption.
        self.write(debt, "changed and still unformatted\n")
        self.assertNotEqual(0, run()) # Byte changes require strict formatting.
        self.write(debt, "unformatted fixture\n")
        self.write("frontend/src/new.ts", "new unformatted\n")
        self.git("add", ".")
        new_tree = self.git("write-tree")
        new_root = subprocess.check_output(["git", "commit-tree", new_tree], cwd=self.repo, input=b"public bootstrap with new file\n").decode().strip()
        self.git("reset", "--hard", new_root)
        event_file.write_text(json.dumps({**event, "after": new_root}))
        self.assertNotEqual(0, run(CI_DIFF_HEAD=new_root)) # New files cannot enter the exception.
        self.git("reset", "--hard", root_head)
        self.write(debt, "changed PR debt\n")
        pr_head = self.save()
        self.assertNotEqual(0, run(GITHUB_EVENT_NAME="pull_request", CI_DIFF_BASE=root_head, CI_DIFF_HEAD=pr_head))
        with self.assertRaises(ValueError):
            formatting.bootstrap("push", {**event, "after": self.git("rev-parse", "HEAD")}, formatting.PUBLIC_REPO,
                                 empty, self.git("rev-parse", "HEAD"), self.repo)

    def test_whitespace_debt_exemption_uses_real_diff_and_bootstrap_guards(self):
        import hashlib
        directory = self.repo / "scripts/ci"
        directory.mkdir(parents=True)
        for filename in ("github-check-whitespace.sh", "github-bootstrap-formatting.py"):
            shutil.copyfile(ROOT / "scripts/ci" / filename, directory / filename)
        debt = "source/debt with spaces.cs"
        self.write(debt, "legacy  \n")
        baseline = {debt: hashlib.sha256((self.repo / debt).read_bytes()).hexdigest()}
        (directory / "github-bootstrap-whitespace-baseline.json").write_text(json.dumps({"files": baseline}))
        def root_commit():
            self.git("add", ".")
            tree = self.git("write-tree")
            head = subprocess.check_output(["git", "commit-tree", tree], cwd=self.repo, input=b"bootstrap fixture\n").decode().strip()
            self.git("reset", "--hard", head)
            return head
        root_head = root_commit()
        empty = subprocess.check_output(["git", "hash-object", "-w", "-t", "tree", "--stdin"], cwd=self.repo, input=b"").decode().strip()
        event_file = self.repo / ".git/event.json"
        def run(head, **changes):
            event_file.write_text(json.dumps({"before": ci.ZERO, "after": head, "ref": "refs/heads/main",
                                               "repository": {"full_name": formatting.PUBLIC_REPO}}))
            env = {**os.environ, "CI_DIFF_BASE": empty, "CI_DIFF_HEAD": head, "GITHUB_EVENT_NAME": "push",
                   "GITHUB_EVENT_PATH": str(event_file), "GITHUB_REPOSITORY": formatting.PUBLIC_REPO, **changes}
            return subprocess.run(["bash", str(directory / "github-check-whitespace.sh")], cwd=self.repo, env=env,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE).returncode
        self.assertEqual(0, run(root_head))
        self.assertNotEqual(0, run(root_head, CI_DIFF_BASE=""))
        self.assertNotEqual(0, run(root_head, CI_DIFF_BASE="f" * 40))
        self.assertNotEqual(0, run(root_head, GITHUB_REPOSITORY="fork/example"))
        self.write(debt, "changed debt   \n")
        self.assertNotEqual(0, run(root_commit()))
        self.git("reset", "--hard", root_head)
        self.write("source/new[1].cs", "new whitespace  \n")
        self.assertNotEqual(0, run(root_commit()))
        self.git("reset", "--hard", root_head)
        self.write(debt, "PR changed debt   \n")
        pr_head = self.save()
        self.assertNotEqual(0, run(pr_head, GITHUB_EVENT_NAME="pull_request", CI_DIFF_BASE=root_head))
        self.assertNotEqual(0, run(pr_head)) # Fake all-zero before on a non-root commit.

    def test_migration_changes_rejected_but_new_corrective_and_snapshot_allowed(self):
        directory = self.repo / "scripts/ci"
        directory.mkdir(parents=True)
        script = directory / "github-check-migrations.sh"
        shutil.copyfile(ROOT / "scripts/ci/github-check-migrations.sh", script)
        for path in ("backend/AuthService/src/Db/Migrations/001.cs", "backend/AuthService/src/Db/Migrations/AppModelSnapshot.cs", "backend/AuthService/src/Web/DataMigrations/Seed.cs"):
            self.write(path, "committed\n")
        base = self.save()
        self.write("backend/AuthService/src/Db/Migrations/002.cs", "corrective\n")
        self.write("backend/AuthService/src/Db/Migrations/AppModelSnapshot.cs", "new snapshot\n")
        head = self.save()
        def check():
            return subprocess.run(["bash", str(script)], cwd=self.repo,
                env={**os.environ, "CI_DIFF_BASE": base, "CI_DIFF_HEAD": self.git("rev-parse", "HEAD")},
                stdout=subprocess.PIPE, stderr=subprocess.PIPE).returncode
        self.assertEqual(0, check())
        for path in ("backend/AuthService/src/Db/Migrations/001.cs", "backend/AuthService/src/Web/DataMigrations/Seed.cs"):
            self.write(path, "edited\n")
            self.save()
            self.assertNotEqual(0, check())
            self.git("reset", "--hard", head)
        self.git("mv", "backend/AuthService/src/Db/Migrations/001.cs", "backend/AuthService/src/Db/Migrations/renamed.cs")
        self.save()
        self.assertNotEqual(0, check())


class Aggregate(unittest.TestCase):
    def needs(self, selected):
        return {"select": {"result": "success", "outputs": {"selected": json.dumps(selected)}},
                "whitespace": {"result": "success"},
                **{k: {"result": "success" if v else "skipped"} for k, v in selected.items()}}

    def test_only_intentionally_unselected_skips_pass(self):
        selected = {k: k == "backend" for k in CONFIG["mandatory"]}
        ci.aggregate(self.needs(selected))
        for key in ("backend", "frontend", "whitespace", "select"):
            for state in ("failure", "cancelled"):
                needs = self.needs(selected)
                needs[key]["result"] = state
                with self.assertRaises(ValueError):
                    ci.aggregate(needs)
        needs = self.needs(selected)
        needs["backend"]["result"] = "skipped"
        with self.assertRaises(ValueError):
            ci.aggregate(needs)

    def test_incomplete_mapping_missing_job_and_invalid_outputs_fail(self):
        selected = {k: True for k in CONFIG["mandatory"]}
        for mutate in (lambda n: n.pop("templates"), lambda n: n["select"]["outputs"].update(selected='{}'),
                       lambda n: n["select"].update(result="skipped")):
            needs = self.needs(selected)
            mutate(needs)
            with self.assertRaises(ValueError):
                ci.aggregate(needs)


class ImageInventory(unittest.TestCase):
    def setUp(self):
        self.sha = "a" * 40
        self.names = {x["name"] for x in CONFIG["images"]}
        self.records = [images.image_record(name, "sha256:" + "b" * 64, self.sha, self.names) for name in self.names]

    def test_complete_immutable_manifest(self):
        self.assertEqual(13, len(images.collect(self.records, self.sha, self.names)["images"]))
        for records in (self.records[:-1], self.records + self.records[:1]):
            with self.assertRaises(ValueError):
                images.collect(records, self.sha, self.names)
        stale = copy.deepcopy(self.records)
        stale[0]["source_sha"] = "c" * 40
        with self.assertRaises(ValueError):
            images.collect(stale, self.sha, self.names)

    def test_invalid_tags_and_digest(self):
        for sha, digest in (("latest", "sha256:" + "b" * 64), (self.sha, "latest")):
            with self.assertRaises(ValueError):
                images.image_record("frontend", digest, sha, self.names)


class NoticePackaging(unittest.TestCase):
    def test_nuget_metadata_and_notices_survive_with_filenames_intact(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory) / "packages"
            output = Path(directory) / "notices"
            for name in ("dependency/1.0/LICENSE.txt", "dependency/1.0/THIRD PARTY NOTICE.txt", "dependency/1.0/dependency.nuspec", "dependency/1.0/library.dll"):
                file = cache / name
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text(name)
            subprocess.run(["sh", str(ROOT / "backend/docker/collect-dependency-notices.sh")], check=True,
                           env={**os.environ, "NUGET_PACKAGES": str(cache), "IMAGE_NOTICES_OUTPUT": str(output)})
            self.assertEqual((cache / "dependency/1.0/LICENSE.txt").read_bytes(), (output / "nuget/dependency/1.0/LICENSE.txt").read_bytes())
            self.assertTrue((output / "nuget/dependency/1.0/THIRD PARTY NOTICE.txt").is_file())
            self.assertTrue((output / "nuget/dependency/1.0/dependency.nuspec").is_file())
            self.assertFalse((output / "nuget/dependency/1.0/library.dll").exists())


class FixtureIsolation(unittest.TestCase):
    def test_prettier_fixture_ignores_outer_github_event_and_diff(self):
        result = subprocess.run(
            ["bash", str(ROOT / "scripts/ci/test-prettier-check-diff.sh")],
            cwd=ROOT,
            env={**os.environ, "GITHUB_EVENT_NAME": "push",
                 "GITHUB_EVENT_PATH": "/nonexistent/outer-github-event.json",
                 "GITHUB_REPOSITORY": formatting.PUBLIC_REPO,
                 "CI_DIFF_BASE": "f" * 40, "CI_DIFF_HEAD": "e" * 40},
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


class WorkflowContract(unittest.TestCase):
    def test_aggregate_and_trusted_publish_boundaries(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text()
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("paths:", workflow)
        self.assertNotIn("paths-ignore:", workflow)
        aggregate = workflow.split("  required-checks:\n", 1)[1].split("  publish-images:\n", 1)[0]
        self.assertIn("    if: always()", aggregate)
        import re
        needs_match = re.search(r"    needs:\s*\[([^\]]+)\]", aggregate, re.MULTILINE)
        self.assertIsNotNone(needs_match, "aggregate must declare its mandatory dependencies")
        needs = re.sub(r"\s+", "", needs_match.group(1)).rstrip(",").split(",")
        self.assertEqual({"select", "whitespace", *CONFIG["mandatory"]}, set(needs))
        publish = workflow.split("  publish-images:\n", 1)[1]
        for boundary in ("github.event_name == 'push'", "github.ref == 'refs/heads/main'", "github.repository == 'KirillSachkov/education-platform'"):
            self.assertIn(boundary, publish)
        checks = workflow.split("  publish-images:\n", 1)[0]
        self.assertNotIn("secrets.", checks)
        self.assertNotIn("packages: write", checks)
        self.assertNotIn("environment:", workflow)
        self.assertIn("contents: read", workflow)
        self.assertIn("persist-credentials: false", workflow)
        self.assertIn("images_matrix: ${{ steps.paths.outputs.images_matrix }}", workflow)
        for filename in ("ci.yml", "integration.yml"):
            content = (ROOT / ".github/workflows" / filename).read_text()
            for action in re.findall(r"uses: ([^\s#]+)", content):
                self.assertTrue(action.startswith("./") or re.fullmatch(r"(?:actions|docker)/[a-z-]+@[0-9a-f]{40}", action), action)
            self.assertNotIn("self-hosted", content)
        for token in ("latest", "education-platform-legacy", "DEPLOY_HOST", "HTTP_PROXY", "TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX"):
            self.assertNotIn(token, workflow)


if __name__ == "__main__":
    unittest.main()
