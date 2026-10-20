import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import check_health as monitor


class HealthMonitor(unittest.TestCase):
    def test_container_identity_and_health_fail_closed(self):
        item = {"Name": "/app", "Config": {"Labels": {"com.docker.compose.project": "platform",
                "com.docker.compose.service": "app"}}, "State": {"Running": True, "Health": {"Status": "healthy"}}}
        self.assertEqual(monitor.container_failures([item], {"app": "app"}, "platform"), [])
        for failure in ["stopped", "unhealthy", "foreign-project", "missing"]:
            copy = json.loads(json.dumps(item))
            if failure == "stopped": copy["State"]["Running"] = False
            if failure == "unhealthy": copy["State"]["Health"]["Status"] = "unhealthy"
            if failure == "foreign-project": copy["Config"]["Labels"]["com.docker.compose.project"] = "other"
            self.assertEqual(monitor.container_failures([] if failure == "missing" else [copy], {"app": "app"}, "platform"), ["container:app"])

    def test_backup_receipt_expiry_future_and_incomplete(self):
        receipt = {"verified_at": 100, "size_bytes": 12000, "sha256": "a" * 64}
        self.assertTrue(monitor.backup_fresh(receipt, 100))
        self.assertTrue(monitor.backup_fresh(receipt, 108100))
        self.assertFalse(monitor.backup_fresh(receipt, 108101))
        self.assertFalse(monitor.backup_fresh(receipt, 99))
        self.assertFalse(monitor.backup_fresh({}, 100))
        self.assertFalse(monitor.backup_fresh({**receipt, "size_bytes": 0}, 100))

    def test_assembled_check_reports_runtime_http_backup_and_disk_independently(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = root / ".github-production"; state.mkdir()
            manifest = state / "current.json"
            manifest.write_text(json.dumps({"image_manifest": {"images": [{"name": "frontend"}]}})); manifest.chmod(0o600)
            receipt = root / "backup.json"
            receipt.write_text(json.dumps({"verified_at": 100, "size_bytes": 20000, "sha256": "a" * 64})); receipt.chmod(0o600)
            config = {"root": str(root), "project": "platform", "backup_receipt": str(receipt),
                      "public_url": "https://example.invalid/"}
            names = {"frontend": "frontend", "postgres": "postgres", "rabbitmq": "rabbitmq", "redis-platform": "redis", "nginx": "nginx"}
            containers = [{"Name": "/" + name, "Config": {"Labels": {"com.docker.compose.project": "platform",
                           "com.docker.compose.service": service}}, "State": {"Running": True, "Health": {"Status": "healthy"}}}
                          for name, service in names.items()]
            with patch.object(monitor, "inspect_containers", return_value=containers), patch.object(monitor.urllib.request, "urlopen") as request, patch.object(monitor.os, "statvfs") as disk:
                request.return_value.__enter__.return_value.status = 200
                disk.return_value.f_bavail = 50; disk.return_value.f_blocks = 100
                self.assertEqual(monitor.check(config, 100), [])
                request.side_effect = OSError("endpoint down")
                disk.return_value.f_bavail = 5
                containers[0]["State"]["Running"] = False
                self.assertEqual(monitor.check(config, 108101), ["backup-stale", "container:frontend", "disk-space", "public-http"])

    def test_no_alert_without_explicit_flag_or_when_healthy(self):
        with patch.object(monitor, "check", return_value=["backup-stale"]), patch.object(monitor, "send_alert") as send:
            self.assertEqual(monitor.run({}), 1)
            send.assert_not_called()
        with patch.object(monitor, "check", return_value=[]), patch.object(monitor, "send_alert") as send:
            self.assertEqual(monitor.run({}, notify=True), 0)
            send.assert_not_called()

    def test_alert_failure_remains_failure_and_does_not_leak_webhook(self):
        with patch.object(monitor, "check", return_value=["public-http"]), patch.object(monitor, "send_alert", side_effect=OSError("private URL")):
            self.assertEqual(monitor.run({"alert_webhook_url": "https://example.invalid/private"}, notify=True), 1)

    def test_private_config_rejects_readable_files_and_symlinks(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "monitor.json"
            path.write_text('{}'); path.chmod(0o600)
            self.assertEqual(monitor.private_json(path), {})
            path.chmod(0o644)
            with self.assertRaises(ValueError): monitor.private_json(path)
            path.chmod(0o600)
            link = Path(directory) / "link"; link.symlink_to(path)
            with self.assertRaises(ValueError): monitor.private_json(link)
            path.write_text('[]')
            with self.assertRaises(ValueError): monitor.private_json(path)

    def test_missing_runtime_and_malformed_backup_still_send_alert(self):
        with tempfile.TemporaryDirectory() as directory:
            receipt = Path(directory) / "backup.json"
            receipt.write_text('[]'); receipt.chmod(0o600)
            config = {"root": directory + "/missing", "project": "platform", "backup_receipt": str(receipt),
                      "public_url": "https://example.invalid/", "alert_webhook_url": "https://example.invalid/alert"}
            with patch.object(monitor.urllib.request, "urlopen") as request, patch.object(monitor, "send_alert") as alert:
                request.return_value.__enter__.return_value.status = 200
                self.assertEqual(monitor.run(config, notify=True), 1)
                alert.assert_called_once_with(config["alert_webhook_url"], ["backup-receipt", "disk-space", "runtime-inventory"])

    def test_alert_payload_does_not_include_endpoint_and_rejects_plaintext(self):
        with self.assertRaises(ValueError): monitor.send_alert("http://example.invalid", ["public-http"])
        with patch.object(monitor.urllib.request, "urlopen") as request:
            request.return_value.__enter__.return_value.status = 200
            monitor.send_alert("https://example.invalid/private-token", ["backup-stale"])
            body = json.loads(request.call_args.args[0].data)
            self.assertEqual(body, {"text": "Education platform checks failed: backup-stale"})


if __name__ == "__main__":
    unittest.main()
