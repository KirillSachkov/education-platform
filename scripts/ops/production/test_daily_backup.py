"""Exercise the daily backup with isolated fake Docker/S3; no real provider calls."""

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "backup-s3.sh"


class DailyBackup(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        bin = self.root / "bin"; bin.mkdir()
        self.tool(bin, "docker", '''
import os, sys
if sys.argv[1] == 'ps': print('postgres')
elif sys.argv[1] == 'exec':
    assert 'PRIVATE_PASSWORD' not in ' '.join(sys.argv)
    assert os.environ['PGPASSWORD'] == 'PRIVATE_PASSWORD'
    sys.stdout.buffer.write(os.urandom(20000))
else: raise SystemExit(1)
''')
        self.tool(bin, "stat", '''
import os, sys
print(os.stat(sys.argv[-1]).st_size)
''')
        self.tool(bin, "aws", '''
import hashlib, json, os, pathlib, sys
a=sys.argv[1:]; store=pathlib.Path(os.environ['FAKE_HEAD'])
if a[:2] == ['s3','cp']:
    assert '--acl' in a and a[a.index('--acl')+1] == 'private'
    data=pathlib.Path(a[2]).read_bytes()
    head={'ContentLength':len(data),'Metadata':{'sha256':hashlib.sha256(data).hexdigest()},'VersionId':'synthetic-version'}
    if os.environ.get('BACKUP_FAULT') == 'size': head['ContentLength'] += 1
    if os.environ.get('BACKUP_FAULT') == 'checksum': head['Metadata']['sha256']='bad'
    store.write_text(json.dumps(head))
elif a[:2] == ['s3api','head-object']:
    if os.environ.get('BACKUP_FAULT') == 'read': raise SystemExit(1)
    print(store.read_text())
elif a[:2] != ['s3','ls']: raise SystemExit(1)
''')
        self.receipt = self.root / "state/backup.json"
        self.env = {**os.environ, "PATH": str(bin) + os.pathsep + os.environ["PATH"],
                    "BACKUP_S3_ACCESS_KEY": "synthetic-access", "BACKUP_S3_SECRET_KEY": "synthetic-secret",
                    "POSTGRES_PASSWORD": "PRIVATE_PASSWORD", "BACKUP_TMP_DIR": str(self.root / "dumps"),
                    "BACKUP_RECEIPT_FILE": str(self.receipt), "FAKE_HEAD": str(self.root / "head.json")}

    def tool(self, bin, name, source):
        path = bin / name
        path.write_text("#!" + sys.executable + "\n" + source)
        path.chmod(0o700)

    def test_verified_upload_writes_private_receipt_then_removes_local_dump(self):
        result = subprocess.run(["bash", str(SCRIPT)], env=self.env, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        receipt = json.loads(self.receipt.read_text())
        self.assertEqual(receipt["version_id"], "synthetic-version")
        self.assertGreater(receipt["size_bytes"], 10240)
        self.assertEqual(self.receipt.stat().st_mode & 0o777, 0o600)
        self.assertFalse(list((self.root / "dumps").glob('*.gz')))
        self.assertNotIn(b"PRIVATE_PASSWORD", result.stdout + result.stderr)

    def test_readback_mismatch_preserves_dump_and_prior_receipt(self):
        self.receipt.parent.mkdir(); self.receipt.write_text('prior receipt')
        for fault in ["size", "checksum", "read"]:
            with self.subTest(fault=fault):
                result = subprocess.run(["bash", str(SCRIPT)], env={**self.env, "BACKUP_FAULT": fault}, capture_output=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.receipt.read_text(), 'prior receipt')
                self.assertTrue(list((self.root / "dumps").glob('*.gz')))
