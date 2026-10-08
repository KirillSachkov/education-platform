import contextlib
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import urllib.request
import zipfile

import github_production as transport
from test_release_model import SHA, manifest, roles


def archive(name="image-manifest.json", content=None):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as result:
        result.writestr(name, json.dumps(content or manifest()))
    return output.getvalue()


def build_run():
    return {"id": 123, "head_sha": SHA, "event": "push", "head_branch": "main",
            "path": ".github/workflows/ci.yml", "status": "completed", "conclusion": "success",
            "repository": {"full_name": transport.REPOSITORY}, "head_repository": {"full_name": transport.REPOSITORY}}


class ProductionTransport(unittest.TestCase):
    def test_redirect_removes_bearer_at_another_host(self):
        request = urllib.request.Request("https://api.github.com/original", headers={"Authorization": "Bearer PRIVATE_SENTINEL"})
        redirect = transport.PublicDownloadRedirect()
        changed = redirect.redirect_request(request, None, 302, "Found", {}, "https://artifact.example.test/archive")
        self.assertIsNone(changed.get_header("Authorization"))
        self.assertEqual(changed.full_url, "https://artifact.example.test/archive")
        same = redirect.redirect_request(request, None, 302, "Found", {}, "https://api.github.com/new")
        self.assertEqual(same.get_header("Authorization"), "Bearer PRIVATE_SENTINEL")
        with self.assertRaises(ValueError):
            redirect.redirect_request(request, None, 302, "Found", {}, "http://artifact.example.test/archive")

    def provenance(self, blob=None):
        blob = blob or archive()
        artifact = {"id": 456, "name": "image-manifest-" + SHA, "expired": False,
                    "workflow_run": {"id": 123, "head_sha": SHA}, "digest": "sha256:" + hashlib.sha256(blob).hexdigest()}
        return blob, {"total_count": 1, "artifacts": [artifact]}

    def select(self, run=None, artifacts=None, blob=None):
        original_blob, original_artifacts = self.provenance(blob)
        with patch.object(transport, "github_request", side_effect=[run or build_run(), artifacts or original_artifacts, original_blob]) as calls:
            result = transport.selected_public_release(SHA, "123")
        self.assertEqual(calls.call_args_list[0].args[0], "/actions/runs/123")
        return result

    def test_exact_artifact_from_exact_trusted_build(self):
        self.assertEqual(self.select()["artifact_id"], 456)

    def test_missing_expired_ambiguous_or_wrong_artifact_fails(self):
        for change in ["missing", "duplicate", "expired", "wrong-run", "wrong-sha", "checksum", "too-many"]:
            blob, artifacts = self.provenance()
            if change == "missing": artifacts["artifacts"] = []
            if change == "duplicate": artifacts["artifacts"].append(copy.deepcopy(artifacts["artifacts"][0]))
            if change == "expired": artifacts["artifacts"][0]["expired"] = True
            if change == "wrong-run": artifacts["artifacts"][0]["workflow_run"]["id"] = 124
            if change == "wrong-sha": artifacts["artifacts"][0]["workflow_run"]["head_sha"] = "e" * 40
            if change == "checksum": artifacts["artifacts"][0]["digest"] = "sha256:" + "0" * 64
            if change == "too-many": artifacts["total_count"] = 101
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.select(artifacts=artifacts, blob=blob)

    def test_artifact_zip_cannot_select_extra_or_traversal_files(self):
        for name in ["../image-manifest.json", "/image-manifest.json", "other.json"]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.select(blob=archive(name))
        incomplete = manifest(); incomplete["images"].pop()
        with self.assertRaises(ValueError):
            self.select(blob=archive(content=incomplete))

    def test_selected_source_configuration_rejects_duplicate_and_symlink(self):
        required = {"docker-compose.prod.yml": "services: {}", "nginx.prod.conf": "events {}",
                    "frontend/src/shared/legal/versions.ts": "versions",
                    "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs": "versions",
                    "CHANGELOG.md": "## [1.2.3]\n"}
        for bad in [None, "duplicate", "symlink", "missing", "oversized"]:
            output = io.BytesIO()
            with zipfile.ZipFile(output, "w") as result:
                for name, content in required.items():
                    if bad == "missing" and name == "nginx.prod.conf": continue
                    item = zipfile.ZipInfo("owner-source/" + name)
                    if bad == "symlink" and name == "nginx.prod.conf": item.external_attr = 0o120777 << 16
                    result.writestr(item, content if bad != "oversized" or name != "nginx.prod.conf" else "x" * (512 * 1024 + 1))
                if bad == "duplicate":
                    with contextlib.redirect_stderr(io.StringIO()):
                        result.writestr("owner-source/nginx.prod.conf", "second")
            with patch.object(transport, "github_request", return_value=output.getvalue()):
                if bad:
                    with self.subTest(bad=bad), self.assertRaises(ValueError): transport.selected_configuration(SHA)
                else:
                    selected, version = transport.selected_configuration(SHA)
                    self.assertEqual(version, "1.2.3")
                    self.assertEqual(set(selected), set(required))

    def environment(self):
        return {"GITHUB_REPOSITORY": transport.REPOSITORY, "GITHUB_REF": "refs/heads/main",
                "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_SHA": SHA,
                "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
                "PRODUCTION_OPERATION": "probe", "PRODUCTION_RELEASE": "current",
                "PRODUCTION_SSH_HOST": "192.0.2.22", "PRODUCTION_SSH_KNOWN_HOSTS": "192.0.2.22 ssh-ed25519 EXAMPLE_KEY",
                "PRODUCTION_SSH_PRIVATE_KEY": "PRIVATE_KEY_SENTINEL", "LEGACY_RELEASE_MANIFEST": json.dumps(roles())}

    def test_ssh_uses_pinned_key_and_payload_not_shell_interpolation(self):
        result = subprocess.CompletedProcess([], 0, b'{"status":"PASS","operation":"probe","applications":14,"health_services":13}', b"PRIVATE_ERROR_SENTINEL")
        output = io.StringIO()
        with patch.dict(os.environ, self.environment(), clear=True), patch.object(transport.subprocess, "run", return_value=result) as ssh, contextlib.redirect_stdout(output):
            transport.main()
        command = ssh.call_args.args[0]
        self.assertIn("StrictHostKeyChecking=yes", command)
        self.assertIn("IdentitiesOnly=yes", command)
        self.assertNotIn("PRIVATE_KEY_SENTINEL", " ".join(command))
        self.assertNotIn("PRIVATE_ERROR_SENTINEL", output.getvalue())
        packet = json.loads(ssh.call_args.kwargs["input"])
        self.assertEqual(packet["credentials"], {})
        self.assertEqual(set(packet["scripts"]), {"remote_production.py", "release_model.py", "run-production-migrations.sh", "check-production-rollback.sh", "validate-production-env.sh"})
        self.assertEqual(json.loads(output.getvalue())["health_services"], 13)

    def test_transport_failure_and_count_sentinel_are_never_public(self):
        for result in [subprocess.CompletedProcess([], 255, b"PRIVATE_OUTPUT", b"PRIVATE_ERROR"),
                       subprocess.CompletedProcess([], 0, b'{"status":"PASS","operation":"probe","applications":"PRIVATE_OUTPUT","health_services":13}', b"")]:
            output = io.StringIO()
            with patch.dict(os.environ, self.environment(), clear=True), patch.object(transport.subprocess, "run", return_value=result), contextlib.redirect_stdout(output), self.assertRaises((RuntimeError, ValueError)):
                transport.main()
            self.assertEqual(output.getvalue(), "")

    def test_secret_key_without_final_newline_loads_in_real_openssh(self):
        real_run = subprocess.run
        receipt = subprocess.CompletedProcess([], 0, b'{"status":"PASS","operation":"probe","applications":14,"health_services":13}', b"")
        with tempfile.TemporaryDirectory(prefix="production-key-test-") as directory:
            generated = Path(directory) / "generated"
            real_run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(generated)], check=True, capture_output=True)
            original = generated.read_text().rstrip("\r\n")
            expected_public = real_run(["ssh-keygen", "-y", "-P", "", "-f", str(generated)], check=True, capture_output=True).stdout

            def inspect_identity(command, **kwargs):
                identity = Path(command[command.index("-i") + 1])
                self.assertEqual(identity.stat().st_mode & 0o777, 0o600)
                loaded = real_run(["ssh-keygen", "-y", "-P", "", "-f", str(identity)], check=True, capture_output=True)
                self.assertEqual(loaded.stdout, expected_public)
                return receipt

            for suffix in ("", "\n", "\r\n"):
                environment = self.environment()
                environment["PRODUCTION_SSH_PRIVATE_KEY"] = original + suffix
                with self.subTest(suffix=repr(suffix)), patch.dict(os.environ, environment, clear=True), patch.object(transport.subprocess, "run", side_effect=inspect_identity), contextlib.redirect_stdout(io.StringIO()):
                    transport.main()


if __name__ == "__main__":
    unittest.main()
