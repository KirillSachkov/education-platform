from unittest.mock import patch

from remote_production import CRITICAL, private_environment
from release_model import LEGACY_REGISTRY
from test_remote_production import RemoteFixture


class EnvironmentSnapshot(RemoteFixture):
    def prepare(self):
        with patch("remote_production.shutil.which", return_value="/usr/bin/tool"):
            self.host.baseline()
        self.host.target = {"registry": LEGACY_REGISTRY, "postgres_image": self.host.roles["postgres_image"],
                            "metadata": self.host.roles["roles"]["current"]["metadata"]}

    def snapshot(self, body):
        desired = self.host.runtime_root / "runtime.env"
        desired.write_text(body); desired.chmod(0o600)
        self.host.prepare_env()

    def test_snapshot_keeps_live_env_unchanged_and_pins_restore_image(self):
        self.prepare()
        before = (self.root / ".env").read_bytes()
        self.snapshot(before.decode() + "RESTORE_POSTGRES_IMAGE=unused:old\n")
        candidate = self.run / "candidate.env"
        self.assertEqual((self.root / ".env").read_bytes(), before)
        self.assertEqual(candidate.stat().st_mode & 0o777, 0o600)
        self.assertEqual(candidate.read_text().count("RESTORE_POSTGRES_IMAGE="), 1)
        self.assertIn("RESTORE_POSTGRES_IMAGE=" + self.host.roles["postgres_image"], candidate.read_text())
        self.assertFalse(any(args[0] == "infisical" for args, _ in self.host.calls))

    def test_missing_or_rotated_critical_secret_preserves_live_env(self):
        self.prepare()
        before = (self.root / ".env").read_bytes()
        for body in [before.decode().replace(CRITICAL[0] + "=example-secret\n", ""),
                     before.decode().replace(CRITICAL[0] + "=example-secret", CRITICAL[0] + "=rotated")]:
            with self.subTest(kind="missing" if CRITICAL[0] not in body else "rotation"):
                with self.assertRaises(ValueError): self.snapshot(body)
                self.assertEqual((self.root / ".env").read_bytes(), before)

    def test_missing_readable_symlink_or_duplicate_source_is_refused(self):
        self.prepare()
        desired = self.host.runtime_root / "runtime.env"
        desired.unlink()
        with self.assertRaises(OSError): self.host.prepare_env()
        desired.write_text("KEY=value\n"); desired.chmod(0o644)
        with self.assertRaises(ValueError): self.host.prepare_env()
        desired.unlink(); desired.symlink_to(self.root / ".env")
        with self.assertRaises(OSError): self.host.prepare_env()
        desired.unlink(); desired.write_text("KEY=first\nKEY=second\n"); desired.chmod(0o600)
        with self.assertRaises(ValueError): self.host.prepare_env()
        self.assertFalse((self.run / "candidate.env").exists())

    def test_legacy_option_drift_and_readable_live_file_are_refused(self):
        self.prepare()
        self.host.baseline_env["EXISTING_OPTION"] = "retain"
        with self.assertRaises(ValueError): self.snapshot((self.root / ".env").read_text())
        (self.root / ".env").chmod(0o644)
        with self.assertRaises(ValueError): private_environment(self.root / ".env")
