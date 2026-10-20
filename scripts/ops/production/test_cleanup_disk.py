"""Retention tests never contact Docker, storage or production."""

import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

import cleanup_disk
from release_model import PUBLIC_REGISTRY
from test_release_model import manifest


class DiskRetention(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / ".github-production").mkdir()
        self.config = {"legacy_repositories": ["registry.example:5050/retired/platform/auth-service"], "local_archives": []}
        self.images = {}; self.references = {}; self.calls = []
        for number, name in enumerate(["current", "previous", "pending"], 1):
            source = str(number) * 40
            record = {"source_sha": source, "registry": PUBLIC_REGISTRY, "image_manifest": manifest(source),
                      "postgres_image": "registry.example/postgres@sha256:" + "c" * 64}
            for item in record["image_manifest"]["images"]:
                # Distinct per-release digest, otherwise manifest fixture shares one digest.
                item["digest"] = "sha256:" + str(number) * 64
                item["reference"] = item["tag"].partition(":" + source)[0] + "@" + item["digest"]
                self.add_image("protected-" + name + "-" + item["name"], [item["tag"]], [item["reference"]])
            path = self.root / ".github-production" / (name + ".json")
            path.write_text(json.dumps(record)); path.chmod(0o600)
        self.add_image("postgres", [], [record["postgres_image"]])
        self.add_image("container-image", [PUBLIC_REGISTRY + "/frontend:container"], [])
        self.add_image("candidate", [PUBLIC_REGISTRY + "/frontend:old"], [PUBLIC_REGISTRY + "/frontend@sha256:" + "9" * 64])
        self.add_image("legacy-candidate", [self.config["legacy_repositories"][0] + ":old"], [])
        self.add_image("unknown", ["unrelated.example/app:old"], [])
        self.add_image("mixed", [PUBLIC_REGISTRY + "/frontend:alias", "other.example/app:old"], [])
        self.add_image("dangling", [], [])
        self.add_image("recent", [PUBLIC_REGISTRY + "/frontend:new"], [], recent=True)

    def add_image(self, identifier, tags, digests, recent=False):
        self.images[identifier] = {"Id": identifier, "RepoTags": tags, "RepoDigests": digests,
                                   "Created": "2099-01-01T00:00:00Z" if recent else "2020-01-01T00:00:00Z", "Size": 100}
        for reference in tags + digests: self.references[reference] = identifier

    def command(self, arguments, **kwargs):
        self.calls.append(arguments)
        if arguments[:3] == ["docker", "image", "inspect"]:
            key = arguments[3]; identifier = self.references.get(key, key)
            if identifier not in self.images: raise RuntimeError("missing image")
            return json.dumps([self.images[identifier]]).encode()
        if arguments == ["docker", "ps", "-aq"]: return b"stopped-container"
        if arguments[:2] == ["docker", "inspect"]: return b'[{"Image":"container-image"}]'
        if arguments[:3] == ["docker", "image", "ls"]: return "\n".join(self.images).encode()
        if arguments[:3] == ["docker", "image", "rm"]: return b""
        raise AssertionError(arguments)

    def plan(self):
        return cleanup_disk.inventory(self.root, self.config)

    def test_dry_run_preserves_all_release_container_unknown_and_recent_images(self):
        with patch.object(cleanup_disk, "command", side_effect=self.command): plan = self.plan()
        self.assertEqual({item["id"] for item in plan["images"]}, {"candidate", "legacy-candidate"})
        self.assertFalse(any("rm" in command for command in self.calls))
        self.assertIn("container-image", plan["protected_image_ids"])

    def test_missing_manifest_rollback_image_and_pending_promotion_refuse_cleanup(self):
        with patch.object(cleanup_disk, "command", side_effect=self.command):
            old = self.images.pop("postgres")
            with self.assertRaises(RuntimeError): self.plan()
            self.images["postgres"] = old
            path = self.root / ".github-production/promotion.pending.json"; path.write_text("{}")
            with self.assertRaises(ValueError): self.plan()
            path.unlink(); (self.root / ".github-production/previous.json").unlink()
            with self.assertRaises(OSError): self.plan()
        self.assertFalse(any("rm" in command for command in self.calls))

    def test_apply_requires_matching_inventory_and_uses_no_force_or_volume_prune(self):
        with patch.object(cleanup_disk, "command", side_effect=self.command):
            plan = self.plan()
            with self.assertRaises(ValueError): cleanup_disk.apply(self.root, self.config, "wrong")
            self.assertFalse(any("rm" in command for command in self.calls))
            cleanup_disk.apply(self.root, self.config, plan["plan_sha256"])
        mutations = [command for command in self.calls if "rm" in command]
        self.assertEqual(len(mutations), 2)
        self.assertIn(["docker", "image", "rm", PUBLIC_REGISTRY + "/frontend:old"], mutations)
        self.assertTrue(all(command[:3] == ["docker", "image", "rm"] and "--force" not in command for command in mutations))

    def archive(self):
        directory = self.root / "backups"; directory.mkdir()
        path = directory / "backup_old.sql.gz"; path.write_bytes(b"synthetic archive"); path.chmod(0o600)
        old = time.time() - 10 * 86400; os.utime(path, (old, old))
        item = {"path": str(path), "key": path.name, "bucket": "fixture", "version_id": "v1",
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "size_bytes": path.stat().st_size}
        self.config["local_archives"] = [item]
        return path

    def test_local_deletion_requires_exact_verified_versioned_offsite_copy(self):
        path = self.archive()
        with patch.object(cleanup_disk, "command", side_effect=self.command), patch("environment_file.compose_environment", return_value={}):
            plan = self.plan()
            with patch.object(cleanup_disk, "verify_offsite", side_effect=ValueError("bad readback")):
                with self.assertRaises(ValueError): cleanup_disk.apply(self.root, self.config, plan["plan_sha256"])
            self.assertTrue(path.exists())
            self.assertFalse(any("rm" in command for command in self.calls))
            with patch.object(cleanup_disk, "verify_offsite") as verify:
                cleanup_disk.apply(self.root, self.config, plan["plan_sha256"])
                self.assertEqual(verify.call_count, 1)
            self.assertFalse(path.exists())

    def test_changed_unverified_or_symlinked_archives_are_preserved(self):
        path = self.archive()
        with patch.object(cleanup_disk, "command", side_effect=self.command):
            self.config["local_archives"][0]["version_id"] = "null"
            with self.assertRaises(ValueError): self.plan()
            self.config["local_archives"][0]["version_id"] = "v1"
            path.write_bytes(b"changed")
            old = time.time() - 10 * 86400; os.utime(path, (old, old))
            with self.assertRaises(ValueError): self.plan()
            path.unlink(); path.symlink_to(self.root / ".github-production/current.json")
            with self.assertRaises(ValueError): self.plan()

    def test_offsite_verification_checks_version_full_bytes_and_metadata(self):
        path = self.archive(); item = self.config["local_archives"][0]
        def storage(arguments, **kwargs):
            self.assertIn("--version-id", arguments)
            if "head-object" in arguments:
                return json.dumps({"ContentLength": item["size_bytes"], "Metadata": {"sha256": item["sha256"]}}).encode()
            Path(arguments[-1]).write_bytes(path.read_bytes())
            return b'{"VersionId":"v1"}'
        env = {"BACKUP_S3_ACCESS_KEY": "fixture", "BACKUP_S3_SECRET_KEY": "fixture"}
        with patch.object(cleanup_disk, "command", side_effect=storage): cleanup_disk.verify_offsite(item, env)
        changed = copy.deepcopy(item); changed["sha256"] = "0" * 64
        with patch.object(cleanup_disk, "command", side_effect=storage), self.assertRaises(ValueError): cleanup_disk.verify_offsite(changed, env)

    def test_cli_defaults_to_dry_run_and_requires_hash_for_explicit_apply(self):
        config = self.root / "cleanup.json"; config.write_text(json.dumps(self.config)); config.chmod(0o600)
        arguments = ["cleanup_disk.py", "--root", str(self.root), "--config", str(config)]
        with patch.object(cleanup_disk, "command", side_effect=self.command), patch("sys.argv", arguments), patch("builtins.print") as output:
            cleanup_disk.main()
            self.assertEqual(json.loads(output.call_args.args[0])["mode"], "dry-run")
        with patch.object(cleanup_disk, "command", side_effect=self.command), patch("sys.argv", arguments + ["--apply"]):
            with self.assertRaises(SystemExit): cleanup_disk.main()
        self.assertFalse(any("rm" in command for command in self.calls))
