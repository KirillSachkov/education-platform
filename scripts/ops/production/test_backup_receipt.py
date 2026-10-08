import gzip
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
import urllib.error

import test_remote_production as fixtures


class RetainedBackup(fixtures.RemoteFixture):
    def setUp(self):
        super().setUp()
        self.host.env.update(POSTGRES_USER="fixture", POSTGRES_PASSWORD="PRIVATE_PASSWORD_SENTINEL",
                             BACKUP_S3_ACCESS_KEY="PRIVATE_ACCESS_SENTINEL", BACKUP_S3_SECRET_KEY="PRIVATE_SECRET_SENTINEL",
                             BACKUP_S3_BUCKET="fixture-private-backups", BACKUP_S3_ENDPOINT="https://backup.example.test")
        self.synthetic = self.run / "synthetic-dump"
        self.synthetic.write_bytes(os.urandom(49152))
        self.fault = None
        self.aws_calls = []
        self.dump_calls = []
        self.original_popen = subprocess.Popen
        self.host.command = self.command

    def command(self, arguments, **kwargs):
        if arguments[:2] == ["gzip", "-t"]:
            if self.fault == "gzip": raise RuntimeError("injected gzip validation failure")
            with gzip.open(arguments[2], "rb") as stream: stream.read()
            return b""
        self.assertEqual(arguments[:4], ["aws", "--endpoint-url", "https://backup.example.test", "s3api"])
        action = arguments[4]
        self.aws_calls.append(arguments)
        self.assertNotIn("PRIVATE_SECRET_SENTINEL", " ".join(arguments))
        self.assertEqual(kwargs["env"]["AWS_SECRET_ACCESS_KEY"], "PRIVATE_SECRET_SENTINEL")
        key = arguments[arguments.index("--key") + 1]
        if action == "put-object":
            if self.fault == "upload": raise RuntimeError("injected upload failure")
            self.upload_key = key
            self.uploaded = Path(arguments[arguments.index("--body") + 1]).read_bytes()
            self.assertEqual(arguments[-2:], ["--acl", "private"])
            return b'{"VersionId":"fixture-version"}'
        self.assertEqual(key, self.upload_key)
        self.assertEqual(arguments[arguments.index("--version-id") + 1], "fixture-version")
        if action == "get-object":
            if self.fault == "download": raise RuntimeError("injected download failure")
            content = self.uploaded
            if self.fault == "wrong-hash": content = bytes([content[0] ^ 1]) + content[1:]
            if self.fault == "wrong-size": content = content[:-1]
            Path(arguments[-1]).write_bytes(content)
            return json.dumps({"VersionId": "wrong" if self.fault == "wrong-version" else "fixture-version"}).encode()
        self.assertEqual(action, "get-object-acl")
        grants = [{"Grantee": {"Type": "CanonicalUser", "ID": "owner"}, "Permission": "FULL_CONTROL"}]
        if self.fault in {"AllUsers", "AuthenticatedUsers"}:
            grants.append({"Grantee": {"Type": "Group", "URI": "http://acs.amazonaws.com/groups/global/" + self.fault}, "Permission": "READ"})
        if self.fault == "private-empty-grants": grants = []
        return json.dumps({"Owner": {} if self.fault == "missing-owner" else {"ID": "owner"}, "Grants": grants}).encode()

    def dump(self, arguments, **kwargs):
        self.dump_calls.append(arguments)
        self.assertEqual(arguments[:5], ["docker", "exec", "-e", "PGPASSWORD", "postgres"])
        self.assertIn("--clean", arguments); self.assertIn("--if-exists", arguments)
        self.assertNotIn("PRIVATE_PASSWORD_SENTINEL", " ".join(arguments))
        script = "import pathlib,sys;sys.stdout.buffer.write(pathlib.Path(sys.argv[1]).read_bytes());sys.exit(int(sys.argv[2]))"
        return self.original_popen([sys.executable, "-c", script, str(self.synthetic), "1" if self.fault == "dump" else "0"],
                                   stdout=subprocess.PIPE, env=kwargs["env"])

    def anonymous(self, request, **kwargs):
        self.assertIn("versionId=fixture-version", request.full_url)
        self.assertIn(self.upload_key, request.full_url)
        self.assertEqual(request.get_header("Range"), "bytes=0-0")
        self.assertIsNone(request.get_header("Authorization"))
        status = 404 if self.fault == "anonymous-404" else 403
        if self.fault == "anonymous-allowed":
            return io.BytesIO(b"public byte")
        raise urllib.error.HTTPError(request.full_url, status, "fixture denial", {}, None)

    def test_exact_retained_archive_and_version_are_verified(self):
        with patch("remote_production.subprocess.Popen", side_effect=self.dump), patch("remote_production.urllib.request.urlopen", side_effect=self.anonymous):
            receipt = self.host.backup()
        archive = Path(receipt["archive"])
        self.assertEqual(receipt["status"], "PASS")
        self.assertEqual(receipt["version"], "fixture-version")
        self.assertEqual(archive.stat().st_mode & 0o777, 0o600)
        with gzip.open(archive, "rb") as stream: self.assertEqual(stream.read(), self.synthetic.read_bytes())
        self.assertFalse((self.run / "backup-readback.sql.gz").exists())
        self.assertEqual([arguments[4] for arguments in self.aws_calls], ["put-object", "get-object", "get-object-acl"])

    def test_dump_gzip_upload_readback_acl_and_anonymous_fail_closed(self):
        for fault in ["dump", "small", "gzip", "upload", "download", "wrong-hash", "wrong-size", "wrong-version",
                      "AllUsers", "AuthenticatedUsers", "missing-owner", "anonymous-404", "anonymous-allowed"]:
            self.fault = fault
            self.synthetic.write_bytes(b"too small" if fault == "small" else os.urandom(49152))
            self.aws_calls = []
            receipt = self.run / "backup-receipt.json"
            if receipt.exists(): receipt.unlink()
            with self.subTest(fault=fault), patch("remote_production.subprocess.Popen", side_effect=self.dump), patch("remote_production.urllib.request.urlopen", side_effect=self.anonymous), self.assertRaises((RuntimeError, ValueError)):
                self.host.backup()
            self.assertFalse(receipt.exists())
            if fault in {"dump", "small", "gzip"}: self.assertEqual(self.aws_calls, [])
            # Own unaccepted readback from a failing fixture is not reused.
            path = self.run / "backup-readback.sql.gz"
            if path.exists(): path.unlink()

    def test_yandex_private_empty_grants_still_requires_owner_and_denial(self):
        self.fault = "private-empty-grants"
        with patch("remote_production.subprocess.Popen", side_effect=self.dump), patch("remote_production.urllib.request.urlopen", side_effect=self.anonymous):
            self.assertEqual(self.host.backup()["status"], "PASS")


if __name__ == "__main__":
    unittest.main()
