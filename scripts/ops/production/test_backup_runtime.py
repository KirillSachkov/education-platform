"""Real age round-trip with synthetic private inputs and isolated object storage."""

import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import backup_runtime
import daily_backup


class RuntimeBackup(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.env = self.root / "runtime.env"
        self.write(self.env, "PRIVATE=synthetic-secret\nBACKUP_S3_ACCESS_KEY=fake\nBACKUP_S3_SECRET_KEY=fake\nBACKUP_S3_BUCKET=fake\n")
        self.write(self.root / ".env", "PRIVATE=applied-secret\n")
        self.write(self.root / "releases/current.env", "RELEASE_TAG=synthetic\n")
        self.key = self.root / "identity"
        subprocess.run(["age-keygen", "-o", str(self.key)], check=True, capture_output=True)
        recipient = subprocess.run(["age-keygen", "-y", str(self.key)], check=True, capture_output=True).stdout
        self.recipient = self.root / "recipient"
        self.recipient.write_bytes(recipient); self.recipient.chmod(0o600)

    def write(self, path, body):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body); path.chmod(0o600)

    def retain(self, fault=None):
        stored = {}
        real_command = backup_runtime.command

        def command(arguments, **kwargs):
            if arguments[0] == "age":
                return real_command(arguments, **kwargs)
            self.assertNotIn("synthetic-secret", " ".join(arguments))
            operation = arguments[4]
            if operation == "put-object":
                self.assertEqual(arguments[arguments.index("--acl") + 1], "private")
                data = Path(arguments[arguments.index("--body") + 1]).read_bytes()
                self.assertNotIn(b"synthetic-secret", data)
                stored["data"] = data
                return json.dumps({"VersionId": None if fault == "version" else "v1"}).encode()
            self.assertEqual(arguments[arguments.index("--version-id") + 1], "v1")
            if operation == "head-object":
                return json.dumps({"ContentLength": len(stored["data"]) + (fault == "size"),
                                   "Metadata": {"sha256": hashlib.sha256(stored["data"]).hexdigest()}}).encode()
            if operation == "get-object":
                Path(arguments[-1]).write_bytes(b"damaged" if fault == "readback" else stored["data"])
                return b'{"VersionId":"v1"}'
            raise AssertionError(operation)

        with patch.object(backup_runtime, "command", side_effect=command):
            receipt = backup_runtime.retain(self.root, self.env, self.recipient)
        return receipt, stored["data"]

    def test_encrypted_versioned_round_trip_preserves_manifest_and_private_inputs(self):
        receipt, encrypted = self.retain()
        self.assertEqual(receipt["status"], "PASS")
        clear = subprocess.run(["age", "-d", "-i", str(self.key)], input=encrypted, capture_output=True, check=True).stdout
        with tarfile.open(fileobj=io.BytesIO(clear), mode="r:gz") as archive:
            manifest = json.load(archive.extractfile("manifest.json"))
            self.assertEqual(set(manifest), {"runtime.env", "applied.env", "releases/current.env"})
            for name, expected in manifest.items():
                data = archive.extractfile(name).read()
                self.assertEqual(hashlib.sha256(data).hexdigest(), expected["sha256"])
                self.assertEqual(len(data), expected["size"])
                self.assertEqual(archive.getmember(name).mode, 0o600)
            self.assertEqual(archive.extractfile("runtime.env").read(), self.env.read_bytes())

    def test_missing_version_or_remote_corruption_blocks_success(self):
        for fault in ["version", "size", "readback"]:
            with self.subTest(fault=fault), self.assertRaises(ValueError): self.retain(fault)

    def test_untrusted_recipient_and_missing_recovery_state_block_upload(self):
        self.recipient.chmod(0o666)
        with self.assertRaises(ValueError): self.retain()
        self.recipient.chmod(0o600)
        (self.root / "releases/current.env").unlink()
        with self.assertRaises(ValueError): self.retain()

    def test_recovery_record_permissions_and_symlinks_are_refused(self):
        current = self.root / "releases/current.env"
        current.chmod(0o644)
        with self.assertRaises(ValueError): backup_runtime.snapshot(self.root, self.env)
        current.unlink(); current.symlink_to(self.env)
        with self.assertRaises(OSError): backup_runtime.snapshot(self.root, self.env)

    def test_daily_backup_never_evaluates_shell_and_stops_before_database_on_config_failure(self):
        self.write(self.root / ".env", "PRIVATE='$(touch /should-not-exist)'\n")
        with patch.object(daily_backup.subprocess, "run") as run:
            daily_backup.run(self.root, self.root)
        self.assertEqual(run.call_count, 2)
        self.assertEqual(run.call_args_list[1].kwargs["env"]["PRIVATE"], "$(touch /should-not-exist)")
        with patch.object(daily_backup.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "backup")) as run:
            with self.assertRaises(subprocess.CalledProcessError): daily_backup.run(self.root, self.root)
        self.assertEqual(run.call_count, 1)
