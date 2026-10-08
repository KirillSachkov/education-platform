import gzip
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "restore-s3.sh"


class RestoreReadiness(unittest.TestCase):
    def run_drill(self, final_server):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / "bin"
            tools.mkdir()
            (root / "dump.sql.gz").write_bytes(gzip.compress(b"SELECT 1;\n"))
            commands = {
                "aws": '#!/bin/sh\ncp "$DRILL_TEST_ROOT/dump.sql.gz" "$4"\n',
                "sleep": "#!/bin/sh\nexit 0\n",
                "docker": """#!/usr/bin/env python3
import os,pathlib,sys
root=pathlib.Path(os.environ['DRILL_TEST_ROOT']); a=sys.argv[1:]
with (root/'commands').open('a') as log: log.write(' '.join(a)+'\\n')
if a[0] in ('volume','run','rm'): sys.exit(0)
assert a[0]=='exec'
if 'pg_isready' in a:
    # The initialization server accepts sockets, but disappears before restore.
    if '-h' not in a: (root/'temporary-server').touch(); sys.exit(0)
    assert a[a.index('-h')+1]=='127.0.0.1'
    if not (root/'first-probe').exists(): (root/'first-probe').touch(); sys.exit(1)
    if os.environ['DRILL_FINAL_SERVER']=='no': sys.exit(1)
    (root/'final-server').touch(); sys.exit(0)
assert 'psql' in a
if not (root/'final-server').exists(): sys.exit(2)
if '-i' in a: sys.stdin.read(); sys.exit(0)
sql=a[-1]
if 'FROM pg_database' in sql: print('1')
elif 'FROM pg_namespace' in sql: print('13')
elif 'FROM pg_tables' in sql: print('100')
elif 'UNION ALL' in sql: print('auth.users|1\\neducation.courses|1\\naccess.plan_grants|1')
""",
            }
            for name, content in commands.items():
                path = tools / name
                path.write_text(content)
                path.chmod(0o700)
            environment = dict(os.environ, PATH=str(tools) + ":" + os.environ["PATH"],
                               DRILL_TEST_ROOT=str(root), DRILL_FINAL_SERVER=final_server,
                               BACKUP_S3_ACCESS_KEY="test", BACKUP_S3_SECRET_KEY="test", TMPDIR=str(root))
            result = subprocess.run(["bash", str(SCRIPT), "--drill", "test.sql.gz"],
                                    env=environment, capture_output=True, text=True, timeout=30)
            commands = (root / "commands").read_text()
            return result, commands

    def test_waits_for_final_tcp_server_before_restore(self):
        result, commands = self.run_drill("yes")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("Restore drill passed", result.stdout)
        self.assertEqual(commands.count("pg_isready"), 2)
        self.assertIn("rm -f education-platform-restore-", commands)
        self.assertIn("volume rm education-platform-restore-", commands)

    def test_temporary_server_cannot_start_restore_when_final_server_never_arrives(self):
        result, commands = self.run_drill("no")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("did not become ready", result.stderr)
        self.assertNotIn("psql", commands)
        self.assertEqual(commands.count("pg_isready"), 60)
        self.assertIn("rm -f education-platform-restore-", commands)
        self.assertIn("volume rm education-platform-restore-", commands)
