#!/usr/bin/env python3
"""Back up private runtime inputs and the database without sourcing shell code."""

import argparse
import os
from pathlib import Path
import subprocess
import sys

from environment_file import dotenv, private_environment


def run(root, runtime_root):
    environment = {**os.environ, **dotenv(private_environment(root / ".env"))}
    subprocess.run([sys.executable, str(Path(__file__).with_name("backup_runtime.py")),
                    "--root", str(root), "--env-file", str(runtime_root / "runtime.env"),
                    "--recipient-file", str(runtime_root / "backup-age-recipient"),
                    "--receipt", "/var/lib/education-platform/last-runtime-backup.json"], check=True)
    subprocess.run(["bash", str(root / "scripts/backup-s3.sh")], env=environment, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("/opt/education-platform"))
    parser.add_argument("--runtime-root", type=Path, default=Path("/srv/education-platform"))
    args = parser.parse_args()
    os.umask(0o077)
    try:
        run(args.root, args.runtime_root)
    except (OSError, ValueError, subprocess.SubprocessError):
        raise SystemExit("Daily backup failed; verified receipt was not refreshed")


if __name__ == "__main__":
    main()
