import base64
import copy
import hashlib
import json
from pathlib import Path
import unittest

from release_model import PUBLIC_REGISTRY
import test_remote_production as fixtures


def body(content):
    return {"base64": base64.b64encode(content).decode(), "sha256": hashlib.sha256(content).hexdigest()}


class NormalRuntime(fixtures.RemoteFixture):
    def setUp(self):
        super().setUp()
        self.host.runtime_root = self.root / "private-runtime"
        markdown = self.host.runtime_root / "approved/markdown"
        pdf = self.host.runtime_root / "approved/pdf"
        markdown.mkdir(parents=True); pdf.mkdir()
        self.versions = {"offer": "v2", "privacy": "v3", "consent-pd": "v2", "cookies": "v1", "consent-marketing": "v1"}
        self.front = "frontend/src/shared/legal/versions.ts"
        self.back = "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs"
        frontend = ("export const CURRENT_LEGAL_VERSIONS = " + json.dumps(self.versions) + " as const;\n").encode()
        names = {"offer": "Offer", "privacy": "PrivacyPolicy", "consent-pd": "PersonalDataConsent", "cookies": "CookiesPolicy", "consent-marketing": "MarketingConsent"}
        backend = "\n".join('public const string ' + names[slug] + ' = "' + version + '";' for slug, version in self.versions.items()).encode()
        business = {"name": "Example Operator", "taxId": "000000000001", "registrationId": "000000000000001",
                    "addressLines": ["Example address"], "taxOffice": "Example office", "email": "operator@example.test",
                    "hours": "Example hours", "copyrightName": "Example"}
        business_path = self.host.runtime_root / "approved/business-details.json"
        business_path.write_text(json.dumps(business))
        hashes = {str(business_path): hashlib.sha256(business_path.read_bytes()).hexdigest()}
        for slug, version in self.versions.items():
            for path, content in [(markdown / (slug + "-" + version + ".md"), b"Example approved legal document"),
                                  (pdf / (slug + "-" + version + ".pdf"), b"%PDF-1.7\nExample synthetic PDF")]:
                path.write_bytes(content); hashes[str(path)] = hashlib.sha256(content).hexdigest()
        approval = {"schema_version": 1, "source_sha": "a" * 40,
                    "environment": {"LEGAL_DOCUMENTS_DIR": str(markdown), "LEGAL_PDFS_DIR": str(pdf), "BUSINESS_DETAILS_FILE": str(business_path)},
                    "file_hashes": hashes, "registry_hashes": {self.front: body(frontend)["sha256"], self.back: body(backend)["sha256"]}}
        self.host.target = {"source_sha": "a" * 40, "registry": PUBLIC_REGISTRY, "runtime_approval": approval,
                            "configuration_files": {self.front: body(frontend), self.back: body(backend)}}

    def test_exact_business_and_five_matching_legal_pairs(self):
        self.assertEqual(self.host.normal_runtime(), self.host.target["runtime_approval"])
        self.assertEqual(self.host.legal_versions, self.versions)
        self.assertEqual(sum(arguments[0] == "setpriv" for arguments, _ in self.host.calls), 11)

    def test_wrong_source_registry_hash_file_hash_or_missing_pair_fails(self):
        original = copy.deepcopy(self.host.target)
        for change in ["wrong-source", "wrong-registry", "wrong-file", "missing-pair", "traversal", "wrong-business"]:
            self.host.target = copy.deepcopy(original)
            approval = self.host.target["runtime_approval"]
            if change == "wrong-source": approval["source_sha"] = "e" * 40
            if change == "wrong-registry": approval["registry_hashes"][self.back] = "0" * 64
            if change == "wrong-file": approval["file_hashes"][next(iter(approval["file_hashes"]))] = "0" * 64
            if change == "missing-pair":
                approval["file_hashes"].pop(str(Path(approval["environment"]["LEGAL_PDFS_DIR"]) / "offer-v2.pdf"))
            if change == "traversal": approval["environment"]["LEGAL_DOCUMENTS_DIR"] += "/../markdown"
            if change == "wrong-business":
                path = Path(approval["environment"]["BUSINESS_DETAILS_FILE"])
                path.write_text('{"unexpected":"value"}')
                approval["file_hashes"][str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
            with self.subTest(change=change), self.assertRaises(ValueError): self.host.normal_runtime()

    def test_backend_version_and_unfinished_markdown_are_rejected(self):
        source = self.host.target["configuration_files"][self.back]
        changed = base64.b64decode(source["base64"]).replace(b'Offer = "v2"', b'Offer = "v1"')
        self.host.target["configuration_files"][self.back] = body(changed)
        self.host.target["runtime_approval"]["registry_hashes"][self.back] = body(changed)["sha256"]
        with self.assertRaises(ValueError): self.host.normal_runtime()
        self.host.target["configuration_files"][self.back] = source
        self.host.target["runtime_approval"]["registry_hashes"][self.back] = source["sha256"]
        approval = self.host.target["runtime_approval"]
        path = Path(approval["environment"]["LEGAL_DOCUMENTS_DIR"]) / "offer-v2.md"
        path.write_text("⟦unfinished⟧")
        approval["file_hashes"][str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
        with self.assertRaises(ValueError): self.host.normal_runtime()


if __name__ == "__main__":
    unittest.main()
