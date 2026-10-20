import base64
import hashlib
import json
from unittest.mock import patch

from release_model import MAINTENANCE_SCRIPTS, PUBLIC_REGISTRY
from test_remote_production import RemoteFixture


def item(content):
    return {"base64": base64.b64encode(content).decode(), "sha256": hashlib.sha256(content).hexdigest()}


class MaintenanceDelivery(RemoteFixture):
    def test_selected_scripts_are_versioned_installed_and_preserved_for_recovery(self):
        files = {"docker-compose.prod.yml": item(b"services: {}\n")}
        files.update({name: item(b"# accepted script\n") for name in MAINTENANCE_SCRIPTS})
        self.host.target = {"registry": PUBLIC_REGISTRY, "configuration_files": files,
                            "postgres_image": self.host.roles["postgres_image"]}
        self.host.services = ("frontend",)
        self.host.references = {"frontend": "fixture:frontend"}
        model = {"name": self.host.project, "volumes": {"postgres_data": {"name": self.host.volume}},
                 "services": {"frontend": {"image": "fixture:frontend"}}}
        self.host.current_record = {"effective_compose": model}
        runtime = {"environment": {"LEGAL_DOCUMENTS_DIR": str(self.root / "legal"),
                                   "LEGAL_PDFS_DIR": str(self.root / "pdf"),
                                   "BUSINESS_DETAILS_FILE": str(self.root / "business.json")}}
        with patch.object(self.host, "normal_runtime", return_value=runtime), patch.object(self.host, "compose", return_value=json.dumps(model).encode()):
            self.host.prepare_configuration()
        self.assertTrue(set(MAINTENANCE_SCRIPTS).issubset(self.host.target["config_hashes"]))
        for name in MAINTENANCE_SCRIPTS:
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"old script\n")
        self.host.apply_configuration()
        for name in MAINTENANCE_SCRIPTS:
            path = self.root / name
            self.assertEqual(path.read_bytes(), b"# accepted script\n")
            self.assertEqual(path.stat().st_mode & 0o777, 0o700)
            self.assertEqual((self.run / "pre-config" / name).read_bytes(), b"old script\n")
        self.host.apply_configuration()
        self.assertEqual((self.run / "pre-config" / MAINTENANCE_SCRIPTS[0]).read_bytes(), b"old script\n")
