"""Cold database readback verification on an isolated, disposable PostgreSQL."""

import gzip
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid

from retained_data import SCHEMAS, columns, fingerprints
from retained_data import literal


class DatabaseRecovery:
    def __init__(self, command, run_directory, image, user, database, password):
        self.command = command
        self.run = Path(run_directory)
        self.image = image
        self.user = user
        self.database = database
        self.environment = {**os.environ, "PGPASSWORD": password}

    def query(self, sql, container="postgres"):
        return self.command(["docker", "exec", "-i", "-e", "PGPASSWORD", container,
                             "psql", "-XqAt", "-F", "|", "-v", "ON_ERROR_STOP=1",
                             "-U", self.user, "-d", self.database],
                            env=self.environment, input=sql.encode(), timeout=180).decode().strip()

    def definitions(self):
        retained = json.loads(self.query("SELECT coalesce(json_agg(nspname),'[]') FROM pg_namespace WHERE nspname LIKE 'retained\\_%' ESCAPE '\\';"))
        return columns(self.query, (*SCHEMAS, *retained))

    def no_writers(self):
        remaining = int(self.query("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() "
            "AND backend_type='client backend' AND pid<>pg_backend_pid() AND usename<>'postgres_exporter';"))
        if remaining:
            raise ValueError("database still has application or unknown client connections")

    def verify_readback(self, archive, definitions, expected):
        roles_sql = ("SELECT md5(coalesce(string_agg(row_to_json(x)::text,'' ORDER BY rolname),'')) FROM "
            "(SELECT rolname,rolsuper,rolinherit,rolcreaterole,rolcreatedb,rolcanlogin,rolreplication,rolconnlimit,"
            "rolpassword,rolvaliduntil,rolbypassrls FROM pg_authid) x;")
        settings_sql = ("SELECT md5(coalesce(string_agg(row_to_json(x)::text,'' ORDER BY role_name,database_name),'')) FROM "
            "(SELECT coalesce(r.rolname,'*') AS role_name,coalesce(d.datname,'*') AS database_name,s.setconfig "
            "FROM pg_db_role_setting s LEFT JOIN pg_roles r ON r.oid=s.setrole LEFT JOIN pg_database d ON d.oid=s.setdatabase) x;")
        memberships_sql = ("SELECT md5(coalesce(string_agg(value::text,'' ORDER BY value::text),'')) FROM "
            "(SELECT jsonb_build_object('role',r.rolname,'member',u.rolname,'options',to_jsonb(m)-'oid'-'grantor'-'member'-'roleid') AS value "
            "FROM pg_auth_members m JOIN pg_roles r ON r.oid=m.roleid JOIN pg_roles u ON u.oid=m.member) x;")
        expected_roles = {"definitions": self.query(roles_sql), "settings": self.query(settings_sql), "memberships": self.query(memberships_sql)}
        quoted_user = self.query("SELECT quote_ident(" + literal(self.user) + ");")
        total = int(self.query("SELECT sum(pg_database_size(datname)) FROM pg_database WHERE NOT datistemplate;"))
        if shutil.disk_usage(self.run).free < total * 3 + 512 * 1024 * 1024:
            raise ValueError("insufficient isolated restore headroom")
        name = "production-restore-" + uuid.uuid4().hex
        completed = False
        state_path = self.run / "isolated-restore-state.json"
        if state_path.exists():
            raise ValueError("isolated restore requires a fresh run directory")
        state = {"status": "WORKING", "container": name, "volume": name, "ownership_label": name}
        def record():
            temporary = state_path.with_name(state_path.name + "." + uuid.uuid4().hex)
            descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(descriptor, "w") as stream:
                json.dump(state, stream); stream.flush(); os.fsync(stream.fileno())
            os.replace(temporary, state_path)
        record()
        self.command(["docker", "volume", "create", "--label", "production.restore=" + name, name])
        volume = json.loads(self.command(["docker", "volume", "inspect", name]))[0]
        if volume.get("Labels", {}).get("production.restore") != name:
            raise ValueError("restore volume does not belong to this run")
        self.command(["docker", "run", "-d", "--name", name, "--label", "production.restore=true",
                      "--network", "none", "--memory", "768m", "--cpus", "1",
                      "-e", "POSTGRES_HOST_AUTH_METHOD=trust", "-e", "POSTGRES_USER=" + self.user,
                      "-e", "POSTGRES_DB=postgres", "-v", name + ":/var/lib/postgresql/data", self.image])
        try:
            for attempt in range(90):
                try:
                    # The image's initdb server accepts Unix sockets before it
                    # restarts. TCP is available only on the final server.
                    self.command(["docker", "exec", name, "pg_isready", "-h", "127.0.0.1", "-U", self.user, "-d", "postgres"])
                    break
                except RuntimeError:
                    if attempt == 89:
                        raise
                    time.sleep(1)
            actual = json.loads(self.command(["docker", "inspect", name]))[0]
            if (actual["HostConfig"]["NetworkMode"] != "none" or actual["HostConfig"].get("PortBindings")
                    or actual["Config"].get("Labels", {}).get("production.restore") != "true"):
                raise ValueError("restore isolation changed")
            started = time.monotonic()
            filtered = 0
            bootstrap_statements = 0
            bootstrap_lines = {("DROP ROLE IF EXISTS " + quoted_user + ";").encode(), ("CREATE ROLE " + quoted_user + ";").encode()}
            monitor_lines = {("GRANT pg_monitor TO postgres_exporter WITH INHERIT TRUE GRANTED BY " + grantor + ";").encode()
                             for grantor in {quoted_user, "postgres"}}
            with (self.run / "isolated-restore.log").open("wb") as log:
                process = subprocess.Popen(["docker", "exec", "-i", name, "psql", "-Xq", "-v", "ON_ERROR_STOP=1",
                                            "-U", self.user, "-d", "postgres"], stdin=subprocess.PIPE, stdout=log, stderr=log)
                try:
                    with gzip.open(archive, "rb") as stream:
                        for line in stream:
                            if time.monotonic() - started > 600:
                                raise TimeoutError("isolated restore deadline exceeded")
                            if line.strip() in bootstrap_lines:
                                # The same initdb role already exists. Its ALTER ROLE
                                # statement is retained, including the original password.
                                bootstrap_statements += 1
                                continue
                            if line.strip() in monitor_lines:
                                filtered += 1
                                continue
                            process.stdin.write(line)
                    process.stdin.close()
                    if process.wait(timeout=600) or filtered > 1 or bootstrap_statements != 2:
                        raise ValueError("isolated database restore failed")
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.wait(timeout=10)
            if filtered:
                self.command(["docker", "exec", name, "psql", "-Xq", "-v", "ON_ERROR_STOP=1", "-U", self.user,
                              "-d", "postgres", "-c", "GRANT pg_monitor TO postgres_exporter;"])
            restored_query = lambda sql: self.query(sql, name)
            if fingerprints(restored_query, definitions) != expected:
                raise ValueError("restored retained data differs from quiesced production")
            actual_roles = {"definitions": restored_query(roles_sql), "settings": restored_query(settings_sql), "memberships": restored_query(memberships_sql)}
            if actual_roles != expected_roles:
                raise ValueError("restored cluster role definitions, settings or privileges differ")
            self.no_writers()
            if fingerprints(self.query, definitions) != expected:
                raise ValueError("production changed during isolated restore")
            proof = {"status": "PASS", "objects": len(expected), "network": "none", "ports": False,
                     "seconds": round(time.monotonic() - started, 2), "known_monitor_grants_restored": filtered,
                     "bootstrap_role_statements_reused": bootstrap_statements,
                     "container": name, "volume": name, "source": "exact-version backup readback", "role_definitions_equal": True,
                     "role_settings_and_membership_privileges_equal": True}
            completed = True
            return proof
        finally:
            # Keep a failed drill for diagnosis. Never touch the application volume.
            if completed:
                self.command(["docker", "rm", "-f", name])
                self.command(["docker", "volume", "rm", name])
            state.update(status="PASS" if completed else "FAIL", resources_removed=completed)
            record()
