from unittest.mock import patch

from remote_production import CRITICAL
from release_model import LEGACY_REGISTRY
from test_remote_production import RemoteFixture


class EnvironmentExport(RemoteFixture):
    def prepare(self):
        with patch("remote_production.shutil.which", return_value="/usr/bin/tool"):
            self.host.baseline()
        self.host.target = {"registry": LEGACY_REGISTRY, "postgres_image": self.host.roles["postgres_image"],
                            "metadata": self.host.roles["roles"]["current"]["metadata"]}
        self.host.packet["credentials"] = {"INFISICAL_CLIENT_ID": "fixture-client",
                                           "INFISICAL_CLIENT_SECRET": "fixture-secret",
                                           "INFISICAL_PROJECT_ID": "fixture-project"}

    def export(self, body, token=b"fixture-token"):
        command = self.host.command

        def fake(arguments, **kwargs):
            if arguments[:2] == ["infisical", "login"]:
                self.assertNotIn("fixture-secret", " ".join(arguments))
                self.assertEqual(kwargs["env"]["INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET"], "fixture-secret")
                return token
            if arguments[:2] == ["infisical", "export"]:
                self.assertEqual(kwargs["env"]["INFISICAL_TOKEN"], "fixture-token")
                return body.encode()
            return command(arguments, **kwargs)

        with patch.object(self.host, "command", side_effect=fake):
            self.host.export_env()

    def test_export_keeps_live_env_unchanged_and_pins_restore_image(self):
        self.prepare()
        before = (self.root / ".env").read_bytes()
        self.export(before.decode() + "RESTORE_POSTGRES_IMAGE=unused:old\n")
        candidate = self.run / "candidate.env"
        self.assertEqual((self.root / ".env").read_bytes(), before)
        self.assertEqual(candidate.stat().st_mode & 0o777, 0o600)
        self.assertEqual(candidate.read_text().count("RESTORE_POSTGRES_IMAGE="), 1)
        self.assertIn("RESTORE_POSTGRES_IMAGE=" + self.host.roles["postgres_image"], candidate.read_text())
        self.assertTrue(any("validate-production-env.sh" in " ".join(args) for args, _ in self.host.calls))

    def test_missing_or_rotated_critical_secret_preserves_live_env(self):
        self.prepare()
        before = (self.root / ".env").read_bytes()
        for body in [before.decode().replace(CRITICAL[0] + "=example-secret\n", ""),
                     before.decode().replace(CRITICAL[0] + "=example-secret", CRITICAL[0] + "=rotated")]:
            with self.subTest(body_kind="missing" if CRITICAL[0] not in body else "rotation"):
                with self.assertRaises(ValueError): self.export(body)
                self.assertEqual((self.root / ".env").read_bytes(), before)

    def test_invalid_login_token_prevents_export_and_live_mutation(self):
        self.prepare()
        before = (self.root / ".env").read_bytes()
        for token in [b"", b"unexpected\nsecond-line"]:
            with self.subTest(token_kind="empty" if not token else "multiple-lines"):
                with self.assertRaises(ValueError): self.export(before.decode(), token)
                self.assertFalse((self.run / "candidate.env").exists())
                self.assertEqual((self.root / ".env").read_bytes(), before)

    def test_noncritical_legacy_drift_also_preserves_live_env(self):
        self.prepare()
        self.host.baseline_env["EXISTING_OPTION"] = "retain"
        before = (self.root / ".env").read_bytes()
        with self.assertRaises(ValueError): self.export(before.decode())
        self.assertEqual((self.root / ".env").read_bytes(), before)
