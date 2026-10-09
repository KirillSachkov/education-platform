#!/usr/bin/env python3
"""Host operation for the trusted manual production workflow.

All detailed output belongs to the loader's root-only log. Public output is a
small receipt written only after the complete operation succeeds.
"""

import base64
import copy
import datetime
import fcntl
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import select
import shlex
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from release_model import (
    LEGACY_REGISTRY, PUBLIC_REGISTRY, services_for_registry,
    dispatch_inputs, full_sha, image_manifest, private_roles, strict_json,
)

CRITICAL = (
    "POSTGRES_USER", "POSTGRES_PASSWORD", "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS",
    "TYPESENSE_API_KEY", "BOT__TOKEN", "INFISICAL_AUTH_SECRET", "INFISICAL_ENCRYPTION_KEY",
)


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_private(path, content):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    with temporary.open("xb") as stream:
        stream.write(content.encode() if isinstance(content, str) else content)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.chmod(0o600)
    os.replace(temporary, path)


def write_json(path, value):
    write_private(path, json.dumps(value, indent=2) + "\n")


def dotenv(text):
    """Read single-line dotenv and release metadata without shell evaluation.

    Actual service values use Compose's parser through environment_values; this
    parser also checks the complete key inventory before Compose sees the file.
    """
    result = {}
    for line in text.splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, separator, value = line.partition("=")
        key = key.strip()
        if not separator or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key) or key in result:
            raise ValueError("invalid or duplicate environment key")
        value = value.strip()
        if value.startswith('"'):
            match = re.fullmatch(r'"((?:\\.|[^"\\])*)"(?:\s+#.*)?\s*', value)
            if not match:
                raise ValueError("unsupported quoted environment value")
            escapes = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", '"': '"', "$": "$"}
            value = re.sub(r'\\([nrt\\"$])', lambda item: escapes[item.group(1)], match.group(1))
        elif value.startswith("'"):
            match = re.fullmatch(r"'([^']*)'(?:\s+#.*)?\s*", value)
            if not match:
                raise ValueError("unsupported quoted environment value")
            value = match.group(1)
        else:
            value = re.split(r"\s+#", value, maxsplit=1)[0].rstrip()
        result[key] = value
    return result


def metadata_text(metadata):
    if any(not re.fullmatch(r"[A-Z_]+", key) or "\n" in str(value) or "\r" in str(value)
           for key, value in metadata.items()):
        raise ValueError("invalid release metadata")
    return "".join(key + "=" + str(value) + "\n" for key, value in metadata.items())


class HostOperation:
    def __init__(self, packet, run_directory, root=Path("/opt/education-platform"), runtime_root=Path("/srv/education-platform")):
        self.packet = packet
        self.run = Path(run_directory)
        self.root = Path(root)
        self.runtime_root = Path(runtime_root)
        self.state = self.root / ".github-production"
        self.releases = self.root / "releases"
        self.roles = private_roles(packet["private_roles"])
        self.inputs = dispatch_inputs(packet["operation"], packet["release"],
                                      packet.get("source_sha", ""), packet.get("build_run_id", ""))
        full_sha(packet["adapter_source_sha"])
        if not re.fullmatch(r"[1-9][0-9]*", str(packet["run_id"])) or not re.fullmatch(r"[1-9][0-9]*", str(packet["run_attempt"])):
            raise ValueError("invalid workflow execution identity")
        self.project = self.roles.get("compose_project", "")
        self.volume = self.roles.get("postgres_volume", "")
        if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,62}", self.project) or not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}", self.volume):
            raise ValueError("approved Compose project and database volume required")
        self.env = dict(os.environ)
        self.target = None
        self.services = services_for_registry(LEGACY_REGISTRY)
        self.effective = self.run / "compose.effective.json"

    def command(self, arguments, *, env=None, input=None, capture=True, timeout=180):
        result = subprocess.run(arguments, env=env or self.env, input=input,
                                stdout=subprocess.PIPE if capture else None,
                                stderr=subprocess.PIPE if capture else None, timeout=timeout)
        if result.returncode:
            if capture:
                sys.stdout.buffer.write(result.stdout + result.stderr)
            raise RuntimeError("host command failed")
        return result.stdout if capture else b""

    def inspect(self, name):
        rows = strict_json(self.command(["docker", "inspect", name]))
        if len(rows) != 1 or rows[0]["Name"] != "/" + name:
            raise ValueError("exact container identity required")
        return rows[0]

    def health(self, attempts):
        for attempt in range(attempts):
            healthy = True
            for name in self.services:
                if name == "telegram-bot-service":
                    continue
                container = self.inspect(name)
                labels = container["Config"].get("Labels", {})
                if (labels.get("com.docker.compose.project") != self.project
                    or labels.get("com.docker.compose.service") != name
                    or not container["State"].get("Running")
                    or container["State"].get("Health", {}).get("Status") != "healthy"):
                    healthy = False
            if healthy:
                return
            if attempt + 1 < attempts:
                time.sleep(10)
        raise RuntimeError("exact release-service health gate failed")

    def verify_postgres(self):
        container = self.inspect("postgres")
        if container["Config"].get("Labels", {}).get("com.docker.compose.project") != self.project:
            raise ValueError("database Compose project mismatch")
        mounts = [row for row in container["Mounts"] if row["Destination"] == "/var/lib/postgresql/data"]
        if len(mounts) != 1 or mounts[0].get("Type") != "volume" or mounts[0].get("Name") != self.volume or not mounts[0].get("RW"):
            raise ValueError("approved database volume mismatch")
        if not container["State"].get("Running") or container["State"].get("Health", {}).get("Status") != "healthy":
            raise ValueError("database baseline unhealthy")
        return mounts[0]

    def verify_configuration(self, hashes):
        for name, expected in hashes.items():
            path = self.root / name
            if not path.is_file() or path.is_symlink() or sha256(path) != expected:
                raise ValueError("approved live configuration changed")

    def compose(self, *arguments, file=None, capture=True, timeout=180):
        return self.command(["docker", "compose", "--project-directory", str(self.root), "-p", self.project,
                             "-f", str(file or self.effective), *arguments], capture=capture, timeout=timeout)

    def environment_values(self, path):
        keys = dotenv(path.read_text())
        probe = self.run / "dotenv-compose.json"
        write_json(probe, {"services": {"dotenv-values": {"image": "unused:dotenv-parser", "env_file": [str(path)]}}})
        rendered = strict_json(self.compose("config", "--format", "json", file=probe))
        values = rendered["services"]["dotenv-values"]["environment"]
        if set(values) != set(keys) or any(not isinstance(value, str) for value in values.values()):
            raise ValueError("canonical Compose environment inventory mismatch")
        return values

    def baseline(self):
        for tool in ["docker", "bash", "infisical", "aws", "gzip", "setpriv"]:
            if not shutil.which(tool):
                raise ValueError("required host tool missing")
        self.command(["docker", "version", "--format", "{{.Server.Version}}"])
        self.command(["docker", "compose", "version", "--short"])
        self.postgres_mount = self.verify_postgres()
        transition = self.state / "promotion.pending.json"
        self.pending_path = self.releases / "pending.env"
        if transition.exists() or (self.pending_path.exists() and self.inputs["operation"] != "rollback"):
            raise ValueError("unresolved previous production transition")
        self.current_metadata = dotenv((self.releases / "current.env").read_text())
        record = self.state / "current.json"
        if record.exists():
            self.current_record = strict_json(record.read_bytes())
            if self.current_record["metadata"] != self.current_metadata:
                raise ValueError("current immutable manifest and metadata mismatch")
            baseline_hashes = self.current_record["config_hashes"]
        else:
            self.current_record = None
            approved = self.roles["roles"]["current"]["metadata"]
            for key in ["IMAGE_TAG", "RELEASE_COMMIT_SHA", "RELEASE_PIPELINE_ID", "MEDIA_BINDING_PROTOCOL"]:
                if self.current_metadata.get(key) != approved.get(key):
                    raise ValueError("first migration baseline is not the approved current release")
            baseline_hashes = self.roles["config_hashes"]
        if self.pending_path.exists():
            pending = strict_json((self.state / "pending.json").read_bytes())
            image_manifest(pending["image_manifest"], pending["source_sha"], pending["registry"])
            if dotenv(self.pending_path.read_text()) != pending["metadata"]:
                raise ValueError("unrecognized interrupted deployment metadata")
            next_hashes = pending["config_hashes"]
            for name in set(baseline_hashes) | set(next_hashes):
                path = self.root / name
                allowed = {value for value in [baseline_hashes.get(name), next_hashes.get(name)] if value}
                if not path.exists() and name not in baseline_hashes:
                    continue
                if not path.is_file() or path.is_symlink() or sha256(path) not in allowed:
                    raise ValueError("interrupted deployment has unexplained configuration drift")
            write_private(self.run / "prior-failed-pending.env", self.pending_path.read_bytes())
            write_json(self.run / "prior-failed-pending.json", pending)
        else:
            self.verify_configuration(baseline_hashes)
        original = self.state / "approved-legacy.json"
        if original.exists() and strict_json(original.read_bytes()) != self.roles:
            raise ValueError("approved original legacy roles changed")
        self.baseline_env_text = (self.root / ".env").read_text()
        self.baseline_env = self.environment_values(self.root / ".env")
        if any(not self.baseline_env.get(key) for key in CRITICAL):
            raise ValueError("baseline critical secret missing")
        self.env.update(self.baseline_env)
        baseline_release = self.current_record or {**self.roles["roles"]["current"], "registry": LEGACY_REGISTRY}
        references = image_manifest(baseline_release["image_manifest"], baseline_release["source_sha"], baseline_release["registry"])
        self.services = services_for_registry(baseline_release["registry"])
        if self.inputs["operation"] != "rollback":
            self.health(1)
        for name, reference in references.items() if self.inputs["operation"] != "rollback" else []:
            container = self.inspect(name)
            image = strict_json(self.command(["docker", "image", "inspect", container["Image"]]))[0]
            if not any(row.partition("@")[2] == reference.partition("@")[2] for row in image.get("RepoDigests", [])):
                raise ValueError("actual baseline binary differs from the recorded release")

    def captured_configuration(self):
        stored = self.state / "approved-config.json"
        if stored.exists():
            return strict_json(stored.read_bytes())
        return {name: {"base64": base64.b64encode((self.root / name).read_bytes()).decode(), "sha256": expected}
                for name, expected in self.roles["config_hashes"].items() if not name.startswith("scripts/")}

    def normalize_legacy_configuration(self):
        """Derive a GitLab-independent copy; retain approved source evidence."""
        name = "docker-compose.prod.yml"
        original = self.captured_configuration()[name]
        raw = base64.b64decode(original["base64"], validate=True)
        if original["sha256"] != self.roles["config_hashes"][name] or hashlib.sha256(raw).hexdigest() != original["sha256"]:
            raise ValueError("approved original Compose checksum changed")
        # This literal is an archived migration input, never a pull destination.
        old = b"    image: gitlab-sachkov.ru:5050/miracle-generation/education-platform/postgres-pgvector:pg16\n"
        replacement = ("    image: " + self.roles["postgres_image"] + "\n").encode()
        if raw.count(old) != 1:
            raise ValueError("one approved legacy PostgreSQL image required")
        normalized = raw.replace(old, replacement)
        files = copy.deepcopy(self.target["configuration_files"])
        selected = base64.b64decode(files[name]["base64"], validate=True)
        if selected not in {raw, normalized} or hashlib.sha256(selected).hexdigest() != files[name]["sha256"]:
            raise ValueError("retained legacy Compose differs from approved configuration")
        digest = hashlib.sha256(normalized).hexdigest()
        files[name] = {"base64": base64.b64encode(normalized).decode(), "sha256": digest}
        self.target["configuration_files"] = files
        self.target["legacy_configuration_normalization"] = {"original_sha256": original["sha256"], "derived_sha256": digest}

    def install_recovery_tool(self):
        source = self.run / "restore-s3.sh"
        target = self.root / "scripts/restore-s3.sh"
        if not source.is_file() or source.is_symlink() or target.is_symlink():
            raise ValueError("ordinary reviewed restore script required")
        if target.exists():
            write_private(self.run / "pre-recovery-tool/restore-s3.sh", target.read_bytes())
        write_private(target, source.read_bytes())
        target.chmod(0o700)
        self.command(["bash", str(target), "--help"])
        write_json(self.run / "recovery-tool-receipt.json", {"status": "PASS", "sha256": sha256(target), "mode": "0700"})

    def verify_target_configuration(self):
        files = self.target["configuration_files"]
        if "docker-compose.prod.yml" not in files:
            raise ValueError("target Compose configuration missing")
        for name, item in files.items():
            if name.startswith("/") or ".." in name.split("/"):
                raise ValueError("invalid target configuration path")
            if not isinstance(item, dict) or hashlib.sha256(base64.b64decode(item["base64"], validate=True)).hexdigest() != item["sha256"]:
                raise ValueError("retained target configuration checksum mismatch")

    def choose_target(self):
        release = self.inputs["release"]
        if release in {"current", "previous"}:
            role = self.roles["roles"][release]
            self.target = {**role, "config_hashes": self.roles["config_hashes"],
                           "postgres_image": self.roles["postgres_image"], "registry": LEGACY_REGISTRY,
                           "configuration_files": self.captured_configuration()}
        elif release == "recorded-previous":
            # A failed deploy has not promoted its target. Restore the current
            # healthy record in that case, rather than an even older previous.
            if self.pending_path.exists():
                self.target = self.current_record or {
                    **self.roles["roles"]["current"], "config_hashes": self.roles["config_hashes"],
                    "registry": LEGACY_REGISTRY, "postgres_image": self.roles["postgres_image"],
                    "configuration_files": self.captured_configuration(), "environment_text": self.baseline_env_text}
            else:
                self.target = strict_json((self.state / "previous.json").read_bytes())
        else:
            public = self.packet["public_release"]
            image_manifest(public["image_manifest"], self.inputs["source_sha"])
            if public["source_sha"] != self.inputs["source_sha"] or public["build_run_id"] != self.inputs["build_run_id"]:
                raise ValueError("public build identity mismatch")
            self.target = {"source_sha": public["source_sha"], "image_manifest": public["image_manifest"],
                           "metadata": {"IMAGE_TAG": public["source_sha"], "RELEASE_COMMIT_SHA": public["source_sha"],
                                        "RELEASE_PIPELINE_ID": public["build_run_id"], "DOCKER_REGISTRY": PUBLIC_REGISTRY + "/",
                                        "MEDIA_BINDING_PROTOCOL": "1", "RELEASED_AT": datetime.datetime.now(datetime.timezone.utc).isoformat()},
                           "registry": PUBLIC_REGISTRY, "postgres_image": self.roles["postgres_image"],
                           "configuration_files": self.packet["normal_configuration"],
                           "runtime_approval": self.packet["normal_runtime_approval"]}
        self.verify_target_configuration()
        self.target["execution"] = {"adapter_source_sha": self.packet["adapter_source_sha"],
                                    "run_id": self.packet["run_id"], "run_attempt": self.packet["run_attempt"]}
        references = image_manifest(self.target["image_manifest"], self.target["source_sha"], self.target["registry"])
        self.references = references
        self.services = services_for_registry(self.target["registry"])
        write_private(self.run / "target.env", metadata_text(self.target["metadata"]))
        self.command(["bash", str(self.run / "check-production-rollback.sh"), str(self.releases / "current.env"),
                      str(self.run / "target.env"), str(self.releases / "pending.env")])

    def export_env(self):
        credentials = self.packet["credentials"]
        export_env = {**self.env, "INFISICAL_API_URL": "http://localhost:8080",
                      "INFISICAL_UNIVERSAL_AUTH_CLIENT_ID": credentials["INFISICAL_CLIENT_ID"],
                      "INFISICAL_UNIVERSAL_AUTH_CLIENT_SECRET": credentials["INFISICAL_CLIENT_SECRET"]}
        token = self.command(["infisical", "login", "--method=universal-auth", "--silent", "--plain"], env=export_env).decode().strip()
        if not token or "\n" in token:
            raise ValueError("Infisical login returned invalid token")
        export_env["INFISICAL_TOKEN"] = token
        body = self.command(["infisical", "export", "--projectId=" + credentials["INFISICAL_PROJECT_ID"],
                             "--env=prod", "--format=dotenv"], env=export_env).decode()
        candidate = self.run / "candidate.env"
        write_private(candidate, body)
        parsed = self.environment_values(candidate)
        if any(not parsed.get(key) for key in CRITICAL):
            raise ValueError("candidate critical secret missing")
        if any(parsed.get(key) != self.baseline_env[key] for key in CRITICAL):
            raise ValueError("unexpected critical secret rotation")
        if self.inputs["release"] in {"current", "previous"}:
            for key, value in self.baseline_env.items():
                if key != "RESTORE_POSTGRES_IMAGE" and parsed.get(key) != value:
                    raise ValueError("legacy trial environment drift")
        if self.inputs["release"] == "recorded-previous":
            retained = self.target["environment_text"]
            retained_file = self.run / "retained-rollback.env"
            write_private(retained_file, retained)
            old = self.environment_values(retained_file)
            if any(old.get(key) != parsed[key] for key in CRITICAL):
                raise ValueError("retained rollback secrets differ from live secrets")
            body = retained.rstrip("\n") + "\n" + "\n".join(
                line for line in body.splitlines() if line.partition("=")[0].strip() not in old)
        body = "\n".join(line for line in body.splitlines() if line.partition("=")[0].strip() != "RESTORE_POSTGRES_IMAGE")
        body += "\nRESTORE_POSTGRES_IMAGE=" + self.target["postgres_image"] + "\n"
        write_private(candidate, body)
        self.command(["bash", str(self.run / "validate-production-env.sh"), str(candidate), str(self.root / ".env")])
        self.candidate_env_text = body
        self.target["environment_text"] = body
        self.env.update(self.environment_values(candidate))
        self.env.update(self.target["metadata"])

    def backup(self):
        archive = self.run / ("pre-operation-" + uuid.uuid4().hex + ".sql.gz")
        partial = archive.with_suffix(archive.suffix + ".part")
        environment = {**self.env, "PGPASSWORD": self.env["POSTGRES_PASSWORD"]}
        with partial.open("xb") as target:
            process = subprocess.Popen(["docker", "exec", "-e", "PGPASSWORD", "postgres", "pg_dumpall", "-U",
                                        self.env["POSTGRES_USER"], "--clean", "--if-exists"],
                                       env=environment, stdout=subprocess.PIPE)
            try:
                with gzip.GzipFile(fileobj=target, mode="wb", mtime=0) as compressed:
                    deadline = time.monotonic() + 600
                    while True:
                        remaining = deadline - time.monotonic()
                        if remaining <= 0 or not select.select([process.stdout], [], [], remaining)[0]:
                            raise TimeoutError("database dump deadline exceeded")
                        chunk = os.read(process.stdout.fileno(), 1024 * 1024)
                        if not chunk:
                            break
                        compressed.write(chunk)
                if process.wait(timeout=10):
                    raise RuntimeError("database dump failed")
                target.flush()
                os.fsync(target.fileno())
            finally:
                process.stdout.close()
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=10)
        partial.chmod(0o600)
        if partial.stat().st_size < 10240:
            raise ValueError("database dump suspiciously small")
        self.command(["gzip", "-t", str(partial)])
        os.rename(partial, archive)
        bucket = self.env.get("BACKUP_S3_BUCKET", "education-platform-backups")
        endpoint = self.env.get("BACKUP_S3_ENDPOINT", "https://storage.yandexcloud.net")
        if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]", bucket) or urllib.parse.urlsplit(endpoint).scheme != "https":
            raise ValueError("invalid private backup destination")
        s3env = {**self.env, "AWS_ACCESS_KEY_ID": self.env["BACKUP_S3_ACCESS_KEY"],
                 "AWS_SECRET_ACCESS_KEY": self.env["BACKUP_S3_SECRET_KEY"], "AWS_DEFAULT_REGION": "ru-central1",
                 "AWS_EC2_METADATA_DISABLED": "true"}
        key = "github-production/" + self.packet["run_id"] + "/" + archive.name
        def aws(*arguments):
            return self.command(["aws", "--endpoint-url", endpoint, "s3api", *arguments], env=s3env, timeout=600)
        uploaded = strict_json(aws("put-object", "--bucket", bucket, "--key", key, "--body", str(archive), "--acl", "private"))
        version = uploaded.get("VersionId")
        select_version = ["--version-id", version] if version else []
        downloaded = self.run / "backup-readback.sql.gz"
        readback = strict_json(aws("get-object", "--bucket", bucket, "--key", key, *select_version, str(downloaded)))
        if version and readback.get("VersionId") != version:
            raise ValueError("backup version readback mismatch")
        downloaded.chmod(0o600)
        if downloaded.stat().st_size != archive.stat().st_size or sha256(downloaded) != sha256(archive):
            raise ValueError("exact backup readback mismatch")
        acl = strict_json(aws("get-object-acl", "--bucket", bucket, "--key", key, *select_version))
        grants = acl.get("Grants")
        if not acl.get("Owner", {}).get("ID") or not isinstance(grants, list) or any(row.get("Grantee", {}).get("Type") != "CanonicalUser" for row in grants):
            raise ValueError("backup ACL is not private")
        url = endpoint.rstrip("/") + "/" + bucket + "/" + urllib.parse.quote(key, safe="/")
        if version:
            url += "?" + urllib.parse.urlencode({"versionId": version})
        request = urllib.request.Request(url, method="GET", headers={"Range": "bytes=0-0"})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raise ValueError("backup allows anonymous access")
        except urllib.error.HTTPError as error:
            if error.code != 403:
                raise ValueError("anonymous backup denial not confirmed") from error
        receipt = {"status": "PASS", "archive": str(archive), "bytes": archive.stat().st_size,
                   "sha256": sha256(archive), "bucket": bucket, "key": key, "version": version,
                   "exact_readback": True, "private_acl": True, "anonymous_status": 403}
        write_json(self.run / "backup-receipt.json", receipt)
        downloaded.unlink()
        return receipt

    def pull(self):
        token = self.packet["credentials"]["LEGACY_GHCR_READ_TOKEN"]
        self.command(["docker", "login", "ghcr.io", "--username", "KirillSachkov", "--password-stdin"], input=token.encode())
        rollback = self.current_record or {**self.roles["roles"]["previous"], "registry": LEGACY_REGISTRY}
        references = set(self.references.values()) | set(image_manifest(rollback["image_manifest"], rollback["source_sha"], rollback["registry"]).values())
        if not self.current_record:
            for role in self.roles["roles"].values():
                references.update(image_manifest(role["image_manifest"], role["source_sha"], LEGACY_REGISTRY).values())
        references.add(self.target["postgres_image"])
        for reference in sorted(references):
            self.command(["docker", "pull", reference], timeout=600)
            rows = strict_json(self.command(["docker", "image", "inspect", reference]))
            if len(rows) != 1 or reference not in rows[0].get("RepoDigests", []):
                raise ValueError("pulled immutable digest mismatch")
        for reference in sorted(self.changed_infrastructure_images):
            self.command(["docker", "pull", reference], timeout=600)

    def normal_runtime(self):
        approval = self.target["runtime_approval"]
        if approval.get("source_sha") != self.target["source_sha"] or type(approval.get("schema_version")) is not int or approval.get("schema_version") != 1:
            raise ValueError("normal runtime approval does not select this source")
        required_env = {"LEGAL_DOCUMENTS_DIR", "LEGAL_PDFS_DIR", "BUSINESS_DETAILS_FILE"}
        if set(approval.get("environment", {})) != required_env:
            raise ValueError("private legal and business runtime directories required")
        for key, value in approval["environment"].items():
            if not isinstance(value, str) or not value.startswith(str(self.runtime_root) + "/") or ".." in value.split("/") or not Path(value).exists():
                raise ValueError("invalid approved private runtime path")
        hashes = approval.get("file_hashes", {})
        if not hashes or approval["environment"]["BUSINESS_DETAILS_FILE"] not in hashes:
            raise ValueError("approved private runtime file identities required")
        for name, expected in hashes.items():
            path = Path(name)
            if not name.startswith(str(self.runtime_root) + "/") or ".." in name.split("/") or path.is_symlink() or not path.is_file() or sha256(path) != expected:
                raise ValueError("private runtime file hash mismatch")
            # Actual frontend UID1000 must be able to read every mounted file.
            self.command(["setpriv", "--reuid=1000", "--regid=1000", "--clear-groups", "test", "-r", str(path)])
        configurations = self.target["configuration_files"]
        for name, item in configurations.items():
            if name.startswith("/") or ".." in name.split("/"):
                raise ValueError("invalid configuration destination")
            content = base64.b64decode(item["base64"], validate=True)
            if hashlib.sha256(content).hexdigest() != item["sha256"]:
                raise ValueError("configuration checksum mismatch")
        registries = ["frontend/src/shared/legal/versions.ts", "backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs"]
        for registry in registries:
            if approval.get("registry_hashes", {}).get(registry) != configurations[registry]["sha256"]:
                raise ValueError("both legal registries must match the approved runtime handoff")
        business = strict_json(Path(approval["environment"]["BUSINESS_DETAILS_FILE"]).read_bytes())
        keys = {"name", "taxId", "registrationId", "addressLines", "taxOffice", "email", "hours", "copyrightName"}
        if (set(business) != keys or any(not isinstance(business[key], str) or not 1 <= len(business[key]) <= 256 for key in keys - {"addressLines"})
            or not re.fullmatch(r"[0-9]{12}", business["taxId"]) or not re.fullmatch(r"[0-9]{15}", business["registrationId"])
            or not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", business["email"])
            or not isinstance(business["addressLines"], list) or not 1 <= len(business["addressLines"]) <= 4
            or any(not isinstance(line, str) or not 1 <= len(line) <= 256 for line in business["addressLines"])):
            raise ValueError("approved eight-field business runtime required")
        frontend = base64.b64decode(configurations[registries[0]]["base64"]).decode()
        backend = base64.b64decode(configurations[registries[1]]["base64"]).decode()
        block = re.search(r"CURRENT_LEGAL_VERSIONS\s*=\s*\{([^}]+)\}", frontend)
        if not block:
            raise ValueError("frontend legal version registry missing")
        versions = dict(re.findall(r'([\w-]+|"[\w-]+")\s*:\s*"(v[1-9][0-9]*)"', block.group(1)))
        versions = {slug.strip('"'): version for slug, version in versions.items()}
        backend_names = {"offer": "Offer", "privacy": "PrivacyPolicy", "consent-pd": "PersonalDataConsent",
                         "cookies": "CookiesPolicy", "consent-marketing": "MarketingConsent"}
        if set(versions) != set(backend_names):
            raise ValueError("five current legal versions required")
        for slug, field in backend_names.items():
            if not re.search(r'public const string ' + field + r'\s*=\s*"' + versions[slug] + r'";', backend):
                raise ValueError("frontend and backend legal versions differ")
            stem = slug + "-" + versions[slug]
            markdown = Path(approval["environment"]["LEGAL_DOCUMENTS_DIR"]) / (stem + ".md")
            pdf = Path(approval["environment"]["LEGAL_PDFS_DIR"]) / (stem + ".pdf")
            if str(markdown) not in hashes or str(pdf) not in hashes:
                raise ValueError("current legal document pair is not approved")
            text = markdown.read_text()
            if not text.strip() or re.search(r"⟦[^⟧]*⟧", text) or not pdf.read_bytes().startswith(b"%PDF-"):
                raise ValueError("current legal document is empty or unfinished")
        self.legal_versions = versions
        return approval

    def prepare_configuration(self):
        if self.target["registry"] == PUBLIC_REGISTRY:
            self.runtime = self.normal_runtime()
        else:
            # Includes private recorded-previous targets and failed-deploy recovery.
            self.normalize_legacy_configuration()
        configurations = self.target["configuration_files"]
        self.target["config_hashes"] = {name: item["sha256"] for name, item in configurations.items()
                                        if name in {"docker-compose.prod.yml", "nginx.prod.conf"} or name.startswith("docker/")}
        staged_base = self.run / "target-compose.yml"
        write_private(staged_base, base64.b64decode(configurations["docker-compose.prod.yml"]["base64"], validate=True))
        override = {"services": {name: {"image": reference} for name, reference in self.references.items()}}
        for name in (service + "-migrations" for service in self.services if service != "frontend"):
            override["services"][name] = {"image": self.references[name.removesuffix("-migrations")]}
        override["services"]["postgres"] = {"image": self.target["postgres_image"]}
        if self.target["registry"] == PUBLIC_REGISTRY:
            e = self.runtime["environment"]
            override["services"]["frontend"].update({"environment": {
                "LEGAL_DOCUMENTS_DIR": "/run/legal/markdown", "LEGAL_PDFS_DIR": "/run/legal/pdf",
                "BUSINESS_DETAILS_FILE": "/run/legal/business-details.json"}, "volumes": [
                    {"type": "bind", "source": e["LEGAL_DOCUMENTS_DIR"], "target": "/run/legal/markdown", "read_only": True},
                    {"type": "bind", "source": e["LEGAL_PDFS_DIR"], "target": "/run/legal/pdf", "read_only": True},
                    {"type": "bind", "source": e["BUSINESS_DETAILS_FILE"], "target": "/run/legal/business-details.json", "read_only": True}]})
        write_json(self.run / "compose.override.json", override)
        rendered = self.compose("-f", str(self.run / "compose.override.json"), "config", "--no-env-resolution", "--format", "json", file=staged_base)
        config = strict_json(rendered)
        if config.get("name") != self.project or config.get("volumes", {}).get("postgres_data", {}).get("name") != self.volume:
            raise ValueError("effective Compose would change the database project or volume")
        for name, reference in self.references.items():
            if config["services"][name]["image"] != reference:
                raise ValueError("effective application image mismatch")
        write_private(self.effective, rendered)
        self.rendered_config = config
        if self.current_record and self.current_record.get("effective_compose"):
            baseline = self.current_record["effective_compose"]
        else:
            previous_environment = self.env
            try:
                self.env = {**self.env, **self.current_metadata}
                baseline = strict_json(self.compose("config", "--no-env-resolution", "--format", "json",
                                                   file=self.root / "docker-compose.prod.yml"))
            finally:
                self.env = previous_environment
        self.changed_definition_consumers = []
        self.changed_infrastructure_images = set()
        for name, service in config["services"].items():
            if name in self.services or name.removesuffix("-migrations") in self.services or name == "postgres":
                continue
            old = baseline["services"].get(name, {})
            if service != old:
                self.changed_definition_consumers.append(name)
                if service.get("image") and service.get("image") != old.get("image"):
                    self.changed_infrastructure_images.add(service["image"])
        self.target["effective_compose"] = config
        self.target["effective_compose_sha256"] = sha256(self.effective)

    def apply_configuration(self):
        changed = set()
        for name in self.target["config_hashes"]:
            path = self.root / name
            content = base64.b64decode(self.target["configuration_files"][name]["base64"], validate=True)
            if path.exists() and path.read_bytes() == content:
                continue
            if path.exists():
                write_private(self.run / "pre-config" / name, path.read_bytes())
            write_private(path, content)
            changed.add(str(path))
            if name.startswith("docker/") or name == "nginx.prod.conf":
                path.chmod(0o644)
        consumers = set(self.changed_definition_consumers)
        for name, service in self.rendered_config["services"].items():
            if name in self.services or name.removesuffix("-migrations") in self.services:
                continue
            for mount in service.get("volumes", []):
                if mount.get("type") != "bind" or mount.get("target", "").startswith("/docker-entrypoint-initdb.d/"):
                    continue
                source = mount.get("source", "").rstrip("/")
                if any(path == source or path.startswith(source + "/") for path in changed):
                    consumers.add(name)
        self.changed_config_consumers = sorted(consumers)

    def migrate_and_start(self):
        # The existing migration runner uses one -f argument. This wrapper adds
        # the exact same project and directory to every compose invocation.
        docker = shutil.which("docker")
        wrapper = self.run / "docker-with-project"
        body = "#!/usr/bin/env bash\nset -Eeuo pipefail\nif [[ ${1:-} == compose ]]; then\nshift\nexec " + shlex.quote(docker)
        body += " compose --project-directory " + shlex.quote(str(self.root)) + " -p " + shlex.quote(self.project) + ' "$@"\nfi\nexec ' + shlex.quote(docker) + ' "$@"\n'
        write_private(wrapper, body); wrapper.chmod(0o700)
        environment = {**self.env, "DOCKER_BIN": str(wrapper)}
        self.command(["bash", str(self.run / "run-production-migrations.sh"), str(self.effective), str(self.releases / "current.env"),
                      "legacy" if self.target["registry"] == LEGACY_REGISTRY else "source"],
                     env=environment, capture=False, timeout=1500)
        self.compose("up", "-d", "--no-build", "--pull", "never", *self.services, capture=False, timeout=900)
        if self.changed_config_consumers:
            # A file bind keeps the old inode after atomic replacement. Recreate
            # exactly the selected configuration consumers after app readiness.
            self.compose("up", "-d", "--no-build", "--pull", "never", *self.changed_config_consumers, capture=False, timeout=300)
            self.compose("up", "-d", "--no-build", "--pull", "never", "--no-deps", "--force-recreate",
                         *self.changed_config_consumers, capture=False, timeout=300)

    def smoke(self):
        base = self.env.get("NEXT_PUBLIC_APP_URL", self.env.get("FRONTEND_URL", ""))
        if not base:
            base = self.roles.get("public_url", "")
        if urllib.parse.urlsplit(base).scheme != "https":
            raise ValueError("approved public HTTPS site required")
        for path in ["/", "/sitemap.xml", "/.well-known/openid-configuration"]:
            with urllib.request.urlopen(base.rstrip("/") + path, timeout=30) as response:
                content = response.read(2 * 1024 * 1024 + 1)
                if response.status != 200 or len(content) > 2 * 1024 * 1024:
                    raise ValueError("public smoke failed")
            if path.endswith("openid-configuration"):
                if strict_json(content).get("issuer", "").rstrip("/") != base.rstrip("/"):
                    raise ValueError("public OIDC issuer mismatch")
        if self.target["registry"] == PUBLIC_REGISTRY:
            for slug, version in self.legal_versions.items():
                with urllib.request.urlopen(base.rstrip("/") + "/legal/" + slug, timeout=30) as response:
                    if response.status != 200:
                        raise ValueError("delivered current legal page unavailable")
                stem = slug + "-" + version + ".pdf"
                with urllib.request.urlopen(base.rstrip("/") + "/legal-docs/" + stem, timeout=30) as response:
                    body = response.read(10 * 1024 * 1024 + 1)
                expected = self.runtime["file_hashes"][self.runtime["environment"]["LEGAL_PDFS_DIR"] + "/" + stem]
                if hashlib.sha256(body).hexdigest() != expected:
                    raise ValueError("delivered current legal PDF differs from approval")

    def verify_release(self):
        self.verify_postgres()
        container = self.inspect("postgres")
        if container["Config"]["Image"] != self.target["postgres_image"]:
            raise ValueError("running PostgreSQL still references a different registry image")
        image = strict_json(self.command(["docker", "image", "inspect", container["Image"]]))[0]
        if self.target["postgres_image"] not in image.get("RepoDigests", []):
            raise ValueError("running PostgreSQL digest differs from the approved copy")
        for name, expected in self.references.items():
            container = self.inspect(name)
            if container["Config"]["Image"] != expected:
                raise ValueError("running application reference mismatch")
            image = strict_json(self.command(["docker", "image", "inspect", container["Image"]]))[0]
            if expected not in image.get("RepoDigests", []):
                raise ValueError("running application digest mismatch")
        self.verify_configuration(self.target["config_hashes"])

    def promote(self):
        before = self.run / "pre-promotion"
        for name in ["current.env", "previous.env"]:
            path = self.releases / name
            if path.exists():
                write_private(before / name, path.read_bytes())
        previous = self.current_record or {**self.roles["roles"]["current"], "registry": LEGACY_REGISTRY,
                                          "config_hashes": self.roles["config_hashes"], "postgres_image": self.roles["postgres_image"],
                                          "configuration_files": self.captured_configuration(), "environment_text": self.baseline_env_text}
        if self.inputs["release"] == "recorded-previous" and (self.run / "prior-failed-pending.json").exists() and self.target["source_sha"] == previous["source_sha"]:
            path = self.state / "previous.json"
            previous = strict_json(path.read_bytes()) if path.exists() else {
                **self.roles["roles"]["previous"], "registry": LEGACY_REGISTRY,
                "config_hashes": self.roles["config_hashes"], "postgres_image": self.roles["postgres_image"],
                "configuration_files": self.captured_configuration(), "environment_text": self.baseline_env_text}
        for name in ["current.json", "previous.json"]:
            path = self.state / name
            if path.exists():
                write_private(before / name, path.read_bytes())
        transition = self.state / "promotion.pending.json"
        write_json(transition, {"before": previous, "target": self.target, "run_directory": str(self.run)})
        write_json(self.state / "previous.json", previous)
        write_private(self.releases / "previous.env", metadata_text(previous["metadata"]))
        write_json(self.state / "current.json", self.target)
        write_private(self.releases / "current.env", metadata_text(self.target["metadata"]))
        (self.releases / "pending.env").unlink()
        write_json(self.run / "promotion-completed.json", {"status": "PASS", "current": self.target["source_sha"], "previous": previous["source_sha"]})
        transition.unlink()
        (self.state / "pending.json").unlink()

    def execute(self):
        self.baseline()
        if self.inputs["operation"] == "probe":
            return self.public_receipt()
        self.choose_target()
        # Validate normal legal/runtime approval before any live-file mutation.
        if self.target["registry"] == PUBLIC_REGISTRY:
            self.normal_runtime()
        self.export_env()
        self.prepare_configuration()
        self.backup()
        self.pull()
        if not (self.state / "approved-config.json").exists():
            write_json(self.state / "approved-config.json", self.captured_configuration())
        write_json(self.state / "approved-legacy.json", self.roles)
        write_private(self.run / "pre-operation.env", self.baseline_env_text)
        write_private(self.root / ".env", self.candidate_env_text)
        if self.pending_path.exists():
            write_private(self.run / "replaced-pending.env", self.pending_path.read_bytes())
        write_json(self.state / "pending.json", self.target)
        write_private(self.releases / "pending.env", metadata_text(self.target["metadata"]))
        self.apply_configuration()
        write_json(self.run / "target-manifest.json", self.target)
        self.migrate_and_start()
        self.health(12 if self.inputs["operation"] == "rollback" else 36)
        self.smoke()
        self.verify_release()
        self.install_recovery_tool()
        self.promote()
        return self.public_receipt()

    def public_receipt(self):
        receipt = {"status": "PASS", "operation": self.inputs["operation"], "applications": len(self.services), "health_services": len(self.services) - 1}
        if self.inputs["release"] == "normal-public-build":
            receipt.update(source_sha=self.target["source_sha"], version=self.packet["public_version"])
        return receipt


def main():
    os.umask(0o077)
    request = Path(sys.argv[1])
    packet = strict_json(request.read_bytes())
    operation = HostOperation(packet, request.parent)
    operation.state.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (operation.state / "operation.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        receipt = operation.execute()
        write_json(request.parent / "public-result.json", receipt)


if __name__ == "__main__":
    main()
