import base64
import copy
import hashlib
import json
from unittest.mock import patch

from release_model import LEGACY_REGISTRY
from remote_production import write_json
from test_remote_production import RemoteFixture


OLD = b"    image: gitlab-sachkov.ru:5050/miracle-generation/education-platform/postgres-pgvector:pg16\n"


class LegacyConfiguration(RemoteFixture):
    def configure(self, raw=None):
        raw = raw or b"services:\n  postgres:\n" + OLD
        digest = hashlib.sha256(raw).hexdigest()
        self.host.roles["config_hashes"]["docker-compose.prod.yml"] = digest
        files = {"docker-compose.prod.yml": {"base64": base64.b64encode(raw).decode(), "sha256": digest}}
        (self.root / "docker-compose.prod.yml").write_bytes(raw)
        write_json(self.host.state / "approved-config.json", files)
        return files, raw

    def test_current_previous_and_recorded_previous_derive_without_changing_evidence(self):
        for release in ["current", "previous", "recorded-previous"]:
            with self.subTest(release=release):
                files, original = self.configure()
                saved = (self.host.state / "approved-config.json").read_bytes()
                roles_before = copy.deepcopy(self.host.roles)
                selected = self.host.roles["roles"]["previous" if release == "recorded-previous" else release]
                self.host.target = {**copy.deepcopy(selected), "registry": LEGACY_REGISTRY,
                                    "postgres_image": self.host.roles["postgres_image"], "configuration_files": files}
                previous = self.host.state / "previous.json"
                write_json(previous, self.host.target)
                previous_before = previous.read_bytes()
                references = {r["name"]: r["reference"] for r in selected["image_manifest"]["images"]}
                self.host.references = references
                model = {"name": self.host.project, "volumes": {"postgres_data": {"name": self.host.volume}},
                         "services": {name: {"image": value} for name, value in references.items()}}
                model["services"]["postgres"] = {"image": self.host.roles["postgres_image"]}
                self.host.current_record = {"effective_compose": model}
                with patch.object(self.host, "compose", return_value=json.dumps(model).encode()):
                    self.host.prepare_configuration()
                got = self.host.target["configuration_files"]["docker-compose.prod.yml"]
                expected = original.replace(OLD, ("    image: " + self.host.roles["postgres_image"] + "\n").encode())
                self.assertEqual(base64.b64decode(got["base64"]), expected)
                self.assertEqual(self.host.target["config_hashes"]["docker-compose.prod.yml"], hashlib.sha256(expected).hexdigest())
                self.assertEqual(self.host.roles, roles_before)
                self.assertEqual((self.host.state / "approved-config.json").read_bytes(), saved)
                self.assertEqual(previous.read_bytes(), previous_before)
                self.assertEqual((self.root / "docker-compose.prod.yml").read_bytes(), original)
                self.assertEqual(self.host.target["image_manifest"], selected["image_manifest"])
                self.assertEqual(self.host.target["effective_compose"], model)

    def test_normalized_private_record_is_idempotent(self):
        files, original = self.configure()
        self.host.target = {"configuration_files": files}
        self.host.normalize_legacy_configuration()
        first = copy.deepcopy(self.host.target)
        self.host.normalize_legacy_configuration()
        self.assertEqual(self.host.target, first)
        self.assertEqual((self.root / "docker-compose.prod.yml").read_bytes(), original)

    def test_unrecognized_or_tampered_configuration_fails_before_commands(self):
        for case in ["missing", "duplicate", "original-checksum", "selected-drift", "selected-checksum"]:
            with self.subTest(case=case):
                raw = b"services: {}\n" if case == "missing" else b"services:\n  postgres:\n" + OLD * (2 if case == "duplicate" else 1)
                files, _ = self.configure(raw)
                self.host.target = {"configuration_files": copy.deepcopy(files)}
                if case == "original-checksum": self.host.roles["config_hashes"]["docker-compose.prod.yml"] = "0" * 64
                if case == "selected-drift":
                    changed = raw + b"# unexplained edit\n"
                    self.host.target["configuration_files"]["docker-compose.prod.yml"] = {
                        "base64": base64.b64encode(changed).decode(), "sha256": hashlib.sha256(changed).hexdigest()}
                if case == "selected-checksum": self.host.target["configuration_files"]["docker-compose.prod.yml"]["sha256"] = "0" * 64
                before = len(self.host.calls)
                with self.assertRaises(ValueError): self.host.normalize_legacy_configuration()
                self.assertEqual(len(self.host.calls), before)

    def test_reviewed_restore_tool_retains_old_bytes_and_restricts_permissions(self):
        old = self.root / "scripts/restore-s3.sh"
        old.parent.mkdir()
        old.write_bytes(b"old restore helper\n")
        self.host.install_recovery_tool()
        self.assertEqual((self.run / "pre-recovery-tool/restore-s3.sh").read_bytes(), b"old restore helper\n")
        self.assertEqual(old.read_bytes(), (self.run / "restore-s3.sh").read_bytes())
        self.assertEqual(old.stat().st_mode & 0o777, 0o700)
        receipt = json.loads((self.run / "recovery-tool-receipt.json").read_text())
        self.assertEqual(receipt["sha256"], hashlib.sha256(old.read_bytes()).hexdigest())

    def test_restore_tool_cannot_replace_a_foreign_symlink(self):
        foreign = self.root / "foreign"
        foreign.write_bytes(b"preserve\n")
        old = self.root / "scripts/restore-s3.sh"
        old.parent.mkdir()
        old.symlink_to(foreign)
        with self.assertRaises(ValueError): self.host.install_recovery_tool()
        self.assertEqual(foreign.read_bytes(), b"preserve\n")
