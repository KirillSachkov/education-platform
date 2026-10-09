import base64
import copy
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

from release_model import PUBLIC_REGISTRY, services_for_registry
from release_model import LEGACY_HEALTH_SERVICES as HEALTH_SERVICES, LEGACY_REGISTRY, LEGACY_SERVICES as SERVICES
from remote_production import CRITICAL, HostOperation, dotenv, metadata_text, write_json, write_private
from test_release_model import roles


class FixtureHost(HostOperation):
    def __init__(self, packet, run, root):
        super().__init__(packet, run, root)
        self.calls = []
        self.unhealthy = set()
        self.mount_name = self.volume
        self.fail_stage = None
        self.events = []

    def inspect(self, name):
        if name == "postgres":
            return {"Name": "/postgres", "Config": {"Image": self.roles["postgres_image"], "Labels": {"com.docker.compose.project": self.project}},
                    "Image": "CONFIG_ID", "State": {"Running": True, "Health": {"Status": "healthy"}},
                    "Mounts": [{"Type": "volume", "Name": self.mount_name, "Destination": "/var/lib/postgresql/data", "RW": True}]}
        return {"Name": "/" + name, "Config": {"Image": self.roles["roles"]["current"]["image_manifest"]["images"][SERVICES.index(name)]["reference"],
                "Labels": {"com.docker.compose.project": self.project, "com.docker.compose.service": name}},
                "Image": "CONFIG_ID", "State": {"Running": True, "Health": {"Status": "unhealthy" if name in self.unhealthy else "healthy"}}}

    def command(self, arguments, **kwargs):
        self.calls.append((arguments, kwargs))
        if arguments[:3] == ["docker", "image", "inspect"]:
            refs = [row["reference"] for row in self.roles["roles"]["current"]["image_manifest"]["images"]]
            refs += [self.roles["postgres_image"]]
            return json.dumps([{"RepoDigests": refs}]).encode()
        if "dotenv-compose.json" in " ".join(arguments):
            model = json.loads((self.run / "dotenv-compose.json").read_text())
            return json.dumps({"services": {"dotenv-values": {"environment": dotenv(Path(model["services"]["dotenv-values"]["env_file"][0]).read_text())}}}).encode()
        return b""

    def event(self, name):
        self.events.append(name)
        if self.fail_stage == name:
            raise RuntimeError("injected failure")


class RemoteFixture(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "production"
        self.run = self.root / ".github-production/runs/test"
        self.run.mkdir(parents=True)
        (self.run / "restore-s3.sh").write_text("#!/usr/bin/env bash\nset -Eeuo pipefail\n")
        (self.root / "releases").mkdir()
        self.compose = b"services: {}\n"
        (self.root / "docker-compose.prod.yml").write_bytes(self.compose)
        private = roles()
        private.update(compose_project="fixture-platform", postgres_volume="fixture-platform_postgres_data", public_url="https://example.test")
        private["config_hashes"]["docker-compose.prod.yml"] = hashlib.sha256(self.compose).hexdigest()
        self.packet = {"private_roles": private, "operation": "deploy", "release": "current",
                       "adapter_source_sha": "f" * 40, "run_id": "123", "run_attempt": "1", "credentials": {}}
        (self.root / "releases/current.env").write_text(metadata_text(private["roles"]["current"]["metadata"]))
        (self.root / ".env").write_text("".join(key + "=example-secret\n" for key in CRITICAL))
        self.host = FixtureHost(self.packet, self.run, self.root)


class RemoteProduction(RemoteFixture):
    def baseline(self):
        with patch("remote_production.shutil.which", return_value="/usr/bin/tool"):
            self.host.baseline()

    def test_dotenv_preserves_embedded_hash_backslash_and_quoted_escape(self):
        parsed = dotenv('HASH=alpha#omega\nBACKSLASH=alpha\\omega\nESCAPED="alpha\\nomega"\nCOMMENT=alpha # ignored\nLITERAL=\'$(touch /tmp/forbidden)\'\n')
        self.assertEqual(parsed, {"HASH": "alpha#omega", "BACKSLASH": "alpha\\omega", "ESCAPED": "alpha\nomega",
                                  "COMMENT": "alpha", "LITERAL": "$(touch /tmp/forbidden)"})
        with self.assertRaises(ValueError): dotenv("KEY=one\nKEY=two\n")

    def test_probe_is_readonly_and_requires_exact_health(self):
        self.host.inputs = {**self.host.inputs, "operation": "probe"}
        with patch("remote_production.shutil.which", return_value="/usr/bin/tool"):
            self.assertEqual(self.host.execute()["health_services"], 13)
        self.assertFalse(any(any(word in arguments for word in ["login", "pull", "up", "pg_dumpall"]) for arguments, _ in self.host.calls))
        self.assertFalse((self.root / "releases/pending.env").exists())
        self.host.unhealthy.add("file-service")
        with self.assertRaises(RuntimeError): self.baseline()

    def test_wrong_volume_or_captured_config_prevents_operation(self):
        self.host.mount_name = "other-volume"
        with self.assertRaises(ValueError): self.baseline()
        self.host.mount_name = self.host.volume
        (self.root / "docker-compose.prod.yml").write_text("foreign changes")
        with self.assertRaises(ValueError): self.baseline()

    def test_original_previous_role_survives_current_promotion(self):
        self.baseline(); self.host.choose_target()
        self.host.target["environment_text"] = self.host.baseline_env_text
        write_json(self.host.state / "pending.json", self.host.target)
        write_private(self.root / "releases/pending.env", metadata_text(self.host.target["metadata"]))
        self.host.promote()
        self.assertEqual(self.host.roles["roles"]["previous"]["source_sha"], "e" * 40)
        self.assertEqual(dotenv((self.root / "releases/current.env").read_text())["IMAGE_TAG"], "a" * 40)
        self.assertFalse((self.host.state / "promotion.pending.json").exists())
        self.assertTrue((self.run / "pre-promotion/current.env").exists())

    def test_failed_deploy_rollback_uses_current_record_and_allows_partial_apps(self):
        self.baseline(); self.host.choose_target()
        current = copy.deepcopy(self.host.target)
        current["environment_text"] = self.host.baseline_env_text
        write_json(self.host.state / "current.json", current)
        older = copy.deepcopy(current); older.update(self.host.roles["roles"]["previous"])
        write_json(self.host.state / "previous.json", older)
        failed = copy.deepcopy(current); failed["source_sha"] = "c" * 40
        # A pending manifest must be internally consistent before recovery.
        from test_release_model import manifest
        failed["image_manifest"] = manifest("c" * 40, LEGACY_REGISTRY)
        failed["metadata"] = {**failed["metadata"], "IMAGE_TAG": "c" * 40, "RELEASE_COMMIT_SHA": "c" * 40}
        write_json(self.host.state / "pending.json", failed)
        write_private(self.root / "releases/pending.env", metadata_text(failed["metadata"]))
        packet = {**self.packet, "operation": "rollback", "release": "recorded-previous"}
        self.host = FixtureHost(packet, self.run, self.root)
        self.host.unhealthy.update(HEALTH_SERVICES)
        self.baseline(); self.host.choose_target()
        self.assertEqual(self.host.target["source_sha"], "a" * 40)
        self.assertNotEqual(self.host.target["source_sha"], older["source_sha"])
        self.assertTrue((self.run / "prior-failed-pending.env").exists())

    def test_backup_failure_prevents_pull_migrations_start_and_promotion(self):
        for failure in ["export", "configuration", "backup", "pull", "migrations", "health", "smoke", "verify"]:
            host = FixtureHost(self.packet, self.run, self.root)
            host.fail_stage = failure
            host.baseline = lambda: host.event("baseline")
            host.choose_target = lambda: setattr(host, "target", {"registry": LEGACY_REGISTRY, "metadata": {}, "source_sha": "a" * 40})
            host.baseline_env_text = "EXAMPLE=original\n"; host.candidate_env_text = "EXAMPLE=candidate\n"
            host.pending_path = self.root / "releases/pending.env"
            for method, event in [("export_env", "export"), ("prepare_configuration", "configuration"), ("backup", "backup"),
                                  ("pull", "pull"), ("apply_configuration", "apply"), ("migrate_and_start", "migrations"),
                                  ("smoke", "smoke"), ("verify_release", "verify"), ("promote", "promote")]:
                setattr(host, method, lambda event=event: host.event(event))
            host.health = lambda attempts: host.event("health")
            host.captured_configuration = lambda: {}
            with self.subTest(failure=failure), self.assertRaises(RuntimeError): host.execute()
            self.assertNotIn("promote", host.events)
            if failure in {"export", "configuration", "backup", "pull"}:
                self.assertNotIn("migrations", host.events)
                self.assertNotIn("apply", host.events)

    def test_compose_calls_and_migration_wrapper_keep_project_and_directory(self):
        self.host.compose("pull", "auth-service")
        command = self.host.calls[-1][0]
        self.assertEqual(command[:7], ["docker", "compose", "--project-directory", str(self.root), "-p", self.host.project, "-f"])
        self.host.changed_config_consumers = []
        self.host.target = {"registry": LEGACY_REGISTRY}
        self.host.migrate_and_start()
        wrapper = (self.run / "docker-with-project").read_text()
        self.assertIn("--project-directory " + str(self.root), wrapper)
        self.assertIn("-p " + self.host.project, wrapper)
        up = self.host.calls[-1][0]
        self.assertEqual(set(up[-14:]), set(SERVICES))
        self.assertNotIn("--force-recreate", up)

    def test_source_startup_health_and_receipt_exclude_trainer(self):
        self.host.target = {"registry": PUBLIC_REGISTRY}
        self.host.services = services_for_registry(PUBLIC_REGISTRY)
        self.host.changed_config_consumers = []
        self.host.migrate_and_start()
        migration = next(arguments for arguments, _ in self.host.calls if "run-production-migrations.sh" in " ".join(arguments))
        self.assertEqual(migration[-1], "source")
        up = self.host.calls[-1][0]
        self.assertNotIn("trainer-service", up)
        self.assertIn("assignment-review-service", up)
        self.assertIn("access-service", up)
        with patch.object(self.host, "inspect", wraps=self.host.inspect) as inspect:
            self.host.health(1)
        self.assertNotIn("trainer-service", [call.args[0] for call in inspect.call_args_list])
        receipt = self.host.public_receipt()
        self.assertEqual((receipt["applications"], receipt["health_services"]), (13, 12))

    def test_running_postgres_must_use_exact_ghcr_reference_and_digest(self):
        self.baseline(); self.host.choose_target()
        self.host.verify_release()
        original = self.host.inspect
        def old_registry(name):
            result = original(name)
            if name == "postgres": result["Config"]["Image"] = "retired.example.test/postgres:old"
            return result
        self.host.inspect = old_registry
        with self.assertRaises(ValueError): self.host.verify_release()


class NormalRuntime(RemoteFixture):
    """Readiness checks use private synthetic inputs and never contact a host."""

    def setUp(self):
        super().setUp()
        self.runtime = self.root / "private-runtime"
        self.markdown = self.runtime / "markdown"
        self.pdf = self.runtime / "pdf"
        self.markdown.mkdir(parents=True)
        self.pdf.mkdir()
        self.host.runtime_root = self.runtime
        self.versions = {"offer": "v2", "privacy": "v3", "consent-pd": "v2",
                         "cookies": "v1", "consent-marketing": "v1"}
        self.registry_names = {
            "frontend/src/shared/legal/versions.ts": 'export const CURRENT_LEGAL_VERSIONS = {' +
                ','.join('"' + slug + '":"' + version + '"' for slug, version in self.versions.items()) + '};',
            "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs": '\n'.join(
                'public const string ' + field + ' = "' + self.versions[slug] + '";'
                for slug, field in [("offer", "Offer"), ("privacy", "PrivacyPolicy"),
                                    ("consent-pd", "PersonalDataConsent"), ("cookies", "CookiesPolicy"),
                                    ("consent-marketing", "MarketingConsent")])}
        configurations = {name: {"base64": base64.b64encode(text.encode()).decode(),
                                 "sha256": hashlib.sha256(text.encode()).hexdigest()}
                          for name, text in self.registry_names.items()}
        business = self.runtime / "business.json"
        business.write_text(json.dumps({"name": "Example", "taxId": "0" * 12, "registrationId": "0" * 15,
                                       "addressLines": ["Example address"], "taxOffice": "Example office",
                                       "email": "operator@example.test", "hours": "Example hours",
                                       "copyrightName": "Example"}))
        for slug, version in {**self.versions, "offer": "v1"}.items():
            self.write_pair(slug, version)
        self.write_pair("offer", "v2")
        self.approval = {"schema_version": 1, "source_sha": "b" * 40,
                         "environment": {"LEGAL_DOCUMENTS_DIR": str(self.markdown),
                                         "LEGAL_PDFS_DIR": str(self.pdf), "BUSINESS_DETAILS_FILE": str(business)},
                         "registry_hashes": {name: item["sha256"] for name, item in configurations.items()},
                         "file_hashes": {}}
        self.refresh_hashes()
        self.host.target = {"source_sha": "b" * 40, "configuration_files": configurations,
                            "runtime_approval": self.approval}

    def write_pair(self, slug, version):
        (self.markdown / (slug + "-" + version + ".md")).write_text("# Synthetic terms\nComplete text.\n")
        (self.pdf / (slug + "-" + version + ".pdf")).write_bytes(b"%PDF-1.7\nsynthetic fixture\n")

    def refresh_hashes(self):
        self.approval["file_hashes"] = {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                                        for path in self.runtime.rglob("*") if path.is_file()}

    def test_accepts_matching_registries_current_pairs_and_approved_archive(self):
        self.assertEqual(self.host.normal_runtime(), self.approval)
        self.assertEqual(self.host.legal_versions, self.versions)
        checked = [arguments[-1] for arguments, _ in self.host.calls if arguments[0] == "setpriv"]
        self.assertEqual(set(checked), set(self.approval["file_hashes"]))
        self.assertTrue(all(arguments[:6] == ["setpriv", "--reuid=1000", "--regid=1000", "--clear-groups", "test", "-r"]
                            for arguments, _ in self.host.calls))

    def test_rejects_approval_for_another_source_or_registry(self):
        self.approval["source_sha"] = "c" * 40
        with self.assertRaisesRegex(ValueError, "does not select this source"): self.host.normal_runtime()
        self.approval["source_sha"] = "b" * 40
        for name in self.registry_names:
            with self.subTest(registry=name):
                original = self.approval["registry_hashes"][name]
                self.approval["registry_hashes"][name] = "0" * 64
                with self.assertRaisesRegex(ValueError, "both legal registries"): self.host.normal_runtime()
                self.approval["registry_hashes"][name] = original

    def test_rejects_different_frontend_and_backend_versions(self):
        name = "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs"
        text = self.registry_names[name].replace('Offer = "v2"', 'Offer = "v1"').encode()
        self.host.target["configuration_files"][name] = {
            "base64": base64.b64encode(text).decode(), "sha256": hashlib.sha256(text).hexdigest()}
        self.approval["registry_hashes"][name] = hashlib.sha256(text).hexdigest()
        with self.assertRaisesRegex(ValueError, "legal versions differ"): self.host.normal_runtime()

    def test_rejects_missing_current_pair_even_when_archive_exists(self):
        for path in [self.markdown / "offer-v2.md", self.pdf / "offer-v2.pdf"]:
            with self.subTest(path=path.name):
                expected = self.approval["file_hashes"].pop(str(path))
                with self.assertRaisesRegex(ValueError, "pair is not approved"): self.host.normal_runtime()
                self.approval["file_hashes"][str(path)] = expected

    def test_rejects_changed_bytes_including_archived_documents(self):
        for path in [self.markdown / "offer-v2.md", self.pdf / "offer-v1.pdf"]:
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(original + b"changed")
                with self.assertRaisesRegex(ValueError, "file hash mismatch"): self.host.normal_runtime()
                path.write_bytes(original)

    def test_rejects_empty_placeholder_markdown_and_non_pdf_bytes(self):
        for content in ["", "# Terms\n⟦EFFECTIVE DATE⟧"]:
            with self.subTest(content=content):
                (self.markdown / "offer-v2.md").write_text(content)
                self.refresh_hashes()
                with self.assertRaisesRegex(ValueError, "empty or unfinished"): self.host.normal_runtime()
        self.write_pair("offer", "v2")
        (self.pdf / "offer-v2.pdf").write_bytes(b"not a PDF")
        self.refresh_hashes()
        with self.assertRaisesRegex(ValueError, "empty or unfinished"): self.host.normal_runtime()

    def test_rejects_unreadable_uid1000_inputs(self):
        with patch.object(self.host, "command", side_effect=PermissionError("synthetic UID1000 denial")):
            with self.assertRaises(PermissionError): self.host.normal_runtime()

    def test_rejects_invalid_business_record(self):
        business = Path(self.approval["environment"]["BUSINESS_DETAILS_FILE"])
        record = json.loads(business.read_text())
        for patch_record in [{"taxId": "invalid"}, {"email": "invalid"}, {"addressLines": []}, {"extra": "field"}]:
            with self.subTest(patch_record=patch_record):
                business.write_text(json.dumps({**record, **patch_record}))
                self.refresh_hashes()
                with self.assertRaisesRegex(ValueError, "eight-field business runtime"): self.host.normal_runtime()


if __name__ == "__main__":
    unittest.main()
