"""Model the image's socket-only initdb server and its final TCP server."""

import gzip
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from database_recovery import DatabaseRecovery


class RestoreStartup(unittest.TestCase):
    def test_socket_only_initdb_readiness_cannot_start_restore(self):
        for final_server_starts in [True, False]:
            with self.subTest(final_server_starts=final_server_starts), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                archive = root / "backup.sql.gz"
                with gzip.open(archive, "wb") as stream:
                    stream.write(b'DROP ROLE IF EXISTS "fixture";\nCREATE ROLE "fixture";\n')
                ready = False
                probes = 0
                name = None
                removed = []

                def command(arguments, **kwargs):
                    nonlocal ready, probes, name
                    if arguments[:3] == ["docker", "volume", "create"]:
                        name = arguments[-1]
                    elif arguments[:3] == ["docker", "volume", "inspect"]:
                        return json.dumps([{"Labels": {"production.restore": name}}]).encode()
                    elif "pg_isready" in arguments:
                        if "-h" not in arguments:
                            return b"accepting connections on the temporary initdb socket"
                        probes += 1
                        ready = final_server_starts and probes >= 2
                        if not ready:
                            raise RuntimeError("TCP final server has not started")
                    elif arguments[:2] == ["docker", "inspect"]:
                        return b'[{"HostConfig":{"NetworkMode":"none"},"Config":{"Labels":{"production.restore":"true"}}}]'
                    elif "rm" in arguments:
                        removed.append(arguments[-1])
                    return b""

                class RestoreProcess:
                    def __init__(self, *args, **kwargs):
                        if not ready:
                            raise RuntimeError("temporary initdb server restarted before restore")
                        self.stdin = io.BytesIO()
                    def wait(self, **kwargs): return 0
                    def poll(self): return 0

                database = DatabaseRecovery(command, root, "fixture-image", "fixture", "fixture_db", "fixture")
                def query(sql, container=None):
                    if "quote_ident" in sql: return '"fixture"'
                    if "pg_database_size" in sql: return "0"
                    return "unchanged-role-fingerprint"
                database.query = query
                with patch("database_recovery.subprocess.Popen", RestoreProcess), patch("database_recovery.time.sleep"), \
                        patch.object(database, "no_writers"), patch("database_recovery.fingerprints", return_value={}):
                    if final_server_starts:
                        self.assertEqual(database.verify_readback(archive, {}, {})["status"], "PASS")
                        self.assertEqual(probes, 2)
                        self.assertEqual(removed, [name, name])
                    else:
                        with self.assertRaisesRegex(RuntimeError, "TCP final server"):
                            database.verify_readback(archive, {}, {})
                        self.assertFalse(removed)
                state = json.loads((root / "isolated-restore-state.json").read_text())
                self.assertEqual(state["status"], "PASS" if final_server_starts else "FAIL")
