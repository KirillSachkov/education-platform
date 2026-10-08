#!/usr/bin/env python3
"""Trusted-main production transport. Detailed transport output stays private."""

import base64
import hashlib
import io
import ipaddress
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile
import urllib.request
import urllib.parse
import zipfile

from release_model import (
    REPOSITORY, dispatch_inputs, image_manifest, private_roles, strict_json,
    trusted_build_run, trusted_dispatch,
)

ROOT = Path(__file__).resolve().parents[3]
API = "https://api.github.com/repos/" + REPOSITORY
MAX_PACKET = 4 * 1024 * 1024


class PublicDownloadRedirect(urllib.request.HTTPRedirectHandler):
    """API credentials stay on the API host during artifact/source redirects."""

    def redirect_request(self, request, response, code, message, headers, new_url):
        target = urllib.parse.urlsplit(new_url)
        if target.scheme != "https":
            raise ValueError("GitHub download redirect must use HTTPS")
        redirected = super().redirect_request(request, response, code, message, headers, new_url)
        if target.hostname != urllib.parse.urlsplit(request.full_url).hostname:
            redirected.remove_header("Authorization")
        return redirected

# Only reviewed source scripts enter this fixed loader; request values never enter
# its shell command. The host keeps detailed logs with mode 0600.
LOADER = r'''
import os,sys,json,base64,hashlib,pathlib,tempfile,subprocess
os.umask(0o077)
raw=sys.stdin.buffer.read(4194305)
if len(raw)>4194304: raise RuntimeError("packet too large")
packet=json.loads(raw)
root=pathlib.Path("/opt/education-platform/.github-production/runs")
root.mkdir(parents=True,exist_ok=True,mode=0o700)
run=pathlib.Path(tempfile.mkdtemp(prefix="run-",dir=root))
allowed={"remote_production.py","release_model.py","run-production-migrations.sh","check-production-rollback.sh","validate-production-env.sh"}
if set(packet["scripts"])!=allowed: raise RuntimeError("script inventory mismatch")
for name,item in packet.pop("scripts").items():
    content=base64.b64decode(item["base64"],validate=True)
    if hashlib.sha256(content).hexdigest()!=item["sha256"]: raise RuntimeError("script checksum mismatch")
    (run/name).write_bytes(content)
request=run/"request.json"
request.write_text(json.dumps(packet))
with (run/"operation.log").open("w") as log:
    result=subprocess.run([sys.executable,str(run/"remote_production.py"),str(request)],stdout=log,stderr=subprocess.STDOUT)
request.unlink()
receipt=run/"public-result.json"
if result.returncode or not receipt.exists():
    print(json.dumps({"status":"FAIL","stage":"host-operation"}))
    sys.exit(1)
data=json.loads(receipt.read_text())
if set(data)-{"status","operation","applications","health_services","source_sha","version"}: raise RuntimeError("invalid public result")
if type(data.get("applications")) is not int or data["applications"]!=14 or type(data.get("health_services")) is not int or data["health_services"]!=13: raise RuntimeError("invalid public counts")
print(json.dumps(data))
'''


def github_request(path, binary=False):
    request = urllib.request.Request(
        API + path,
        headers={"Authorization": "Bearer " + os.environ["GH_TOKEN"],
                 "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2026-03-10",
                 "User-Agent": "education-platform-production"},
    )
    with urllib.request.build_opener(PublicDownloadRedirect()).open(request, timeout=120) as response:
        body = response.read(128 * 1024 * 1024 + 1)
    if len(body) > 128 * 1024 * 1024:
        raise ValueError("bounded GitHub response exceeded")
    return body if binary else strict_json(body)


def selected_public_release(source_sha, run_id):
    run = github_request("/actions/runs/" + run_id)
    trusted_build_run(run, source_sha, run_id)
    artifacts = github_request("/actions/runs/" + run_id + "/artifacts?per_page=100")
    if artifacts.get("total_count", 0) > 100:
        raise ValueError("ambiguous build artifact inventory")
    matches = [item for item in artifacts["artifacts"] if item["name"] == "image-manifest-" + source_sha]
    if len(matches) != 1 or matches[0].get("expired"):
        raise ValueError("one unexpired exact build manifest required")
    artifact = matches[0]
    origin = artifact.get("workflow_run", {})
    if origin.get("id") != int(run_id) or origin.get("head_sha") != source_sha:
        raise ValueError("artifact build identity mismatch")
    blob = github_request("/actions/artifacts/" + str(artifact["id"]) + "/zip", binary=True)
    if artifact.get("digest") != "sha256:" + hashlib.sha256(blob).hexdigest():
        raise ValueError("artifact archive checksum mismatch")
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        if archive.namelist() != ["image-manifest.json"] or archive.getinfo("image-manifest.json").file_size > 65536:
            raise ValueError("unexpected manifest archive contents")
        manifest = strict_json(archive.read("image-manifest.json"))
    image_manifest(manifest, source_sha)
    return {"source_sha": source_sha, "build_run_id": run_id, "artifact_id": artifact["id"], "image_manifest": manifest}


def selected_configuration(source_sha):
    blob = github_request("/zipball/" + source_sha, binary=True)
    files = {}
    required = {"docker-compose.prod.yml", "nginx.prod.conf", "frontend/src/shared/legal/versions.ts",
                "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs", "CHANGELOG.md"}
    # The selected image SHA also selects its configuration; a newer workflow
    # revision never substitutes pending-main configuration into an older release.
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        prefix = archive.namelist()[0].split("/", 1)[0] + "/"
        for item in archive.infolist():
            if item.is_dir() or not item.filename.startswith(prefix):
                continue
            name = item.filename[len(prefix):]
            if name not in required and not name.startswith("docker/"):
                continue
            if name.startswith("/") or ".." in name.split("/") or (item.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("unsafe selected configuration path")
            if item.file_size > 512 * 1024 or name in files:
                raise ValueError("selected configuration member too large")
            content = archive.read(item)
            files[name] = {"base64": base64.b64encode(content).decode(), "sha256": hashlib.sha256(content).hexdigest()}
    if not required.issubset(files):
        raise ValueError("selected configuration or legal registries missing")
    changelog = base64.b64decode(files["CHANGELOG.md"]["base64"]).decode()
    match = re.search(r"^## \[([0-9]+\.[0-9]+\.[0-9]+)\]", changelog, re.MULTILINE)
    if not match:
        raise ValueError("released semantic version required")
    return files, match.group(1)


def required_secret(name):
    value = os.environ.get(name, "")
    if not value.strip():
        raise ValueError("required production input missing")
    return value


def main():
    os.umask(0o077)
    trusted_dispatch(os.environ)
    inputs = dispatch_inputs(os.environ["PRODUCTION_OPERATION"], os.environ["PRODUCTION_RELEASE"],
                             os.environ.get("PRODUCTION_SOURCE_SHA", ""), os.environ.get("PRODUCTION_BUILD_RUN_ID", ""))
    roles = private_roles(strict_json(required_secret("LEGACY_RELEASE_MANIFEST")))
    host = str(ipaddress.IPv4Address(required_secret("PRODUCTION_SSH_HOST")))
    known_hosts = required_secret("PRODUCTION_SSH_KNOWN_HOSTS")
    lines = [line.split() for line in known_hosts.splitlines() if line.strip()]
    if len(lines) != 1 or len(lines[0]) < 3 or lines[0][0] != host or lines[0][1] != "ssh-ed25519":
        raise ValueError("one independently pinned host key required")
    packet = {**inputs, "adapter_source_sha": os.environ["GITHUB_SHA"],
              "run_id": os.environ["GITHUB_RUN_ID"], "run_attempt": os.environ["GITHUB_RUN_ATTEMPT"],
              "private_roles": roles, "credentials": {}, "scripts": {}}
    if inputs["operation"] != "probe":
        packet["credentials"] = {name: required_secret(name) for name in
                                  ["LEGACY_GHCR_READ_TOKEN", "INFISICAL_CLIENT_ID", "INFISICAL_CLIENT_SECRET", "INFISICAL_PROJECT_ID"]}
    if inputs["release"] == "normal-public-build":
        packet["public_release"] = selected_public_release(inputs["source_sha"], inputs["build_run_id"])
        packet["normal_configuration"], packet["public_version"] = selected_configuration(inputs["source_sha"])
        packet["normal_runtime_approval"] = strict_json(required_secret("NORMAL_RUNTIME_APPROVAL"))
    scripts = {"remote_production.py": Path(__file__).with_name("remote_production.py"),
               "release_model.py": Path(__file__).with_name("release_model.py")}
    scripts.update({name: ROOT / "scripts" / name for name in
                    ["run-production-migrations.sh", "check-production-rollback.sh", "validate-production-env.sh"]})
    for name, path in scripts.items():
        content = path.read_bytes()
        packet["scripts"][name] = {"base64": base64.b64encode(content).decode(), "sha256": hashlib.sha256(content).hexdigest()}
    data = json.dumps(packet).encode()
    if len(data) > MAX_PACKET:
        raise ValueError("bounded production packet exceeded")
    with tempfile.TemporaryDirectory(prefix="github-production-") as directory:
        folder = Path(directory)
        # GitHub CLI trims trailing newlines when setting a secret from stdin.
        # OpenSSH requires the final newline in its private-key file format.
        key = folder / "identity"; key.write_text(required_secret("PRODUCTION_SSH_PRIVATE_KEY").rstrip("\r\n") + "\n"); key.chmod(0o600)
        known = folder / "known_hosts"; known.write_text(known_hosts); known.chmod(0o600)
        command = ["ssh", "-i", str(key), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes",
                   "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + str(known),
                   "-o", "ConnectTimeout=20", "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=4",
                   "root@" + host, "python3 -c " + shlex.quote(LOADER)]
        result = subprocess.run(command, input=data, capture_output=True, timeout=3550)
        (folder / "transport.private.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError("production transport or host operation failed")
        receipt = strict_json(result.stdout)
        if receipt.get("status") != "PASS" or receipt.get("operation") != inputs["operation"]:
            raise ValueError("production receipt mismatch")
        if type(receipt.get("applications")) is not int or receipt["applications"] != 14 or type(receipt.get("health_services")) is not int or receipt["health_services"] != 13:
            raise ValueError("production receipt counts mismatch")
        public = {"status": "PASS", "operation": inputs["operation"], "applications": receipt.get("applications"),
                  "health_services": receipt.get("health_services")}
        if inputs["release"] == "normal-public-build":
            if receipt.get("source_sha") != inputs["source_sha"] or receipt.get("version") != packet["public_version"]:
                raise ValueError("public deployed release identity mismatch")
            with open(os.environ["GITHUB_OUTPUT"], "a") as output:
                output.write("source_sha=" + inputs["source_sha"] + "\nversion=" + packet["public_version"] + "\n")
        print(json.dumps(public))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Host addresses, private source identities, credentials and remote logs
        # never become public Actions diagnostics, including before SSH connects.
        print("Production operation failed; inspect the protected host evidence.", file=sys.stderr)
        sys.exit(1)
