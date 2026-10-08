#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

python3 - nginx.prod.conf <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
source = path.read_text()


def blocks(keyword: str, text: str):
    pattern = re.compile(rf"\b{re.escape(keyword)}\s+([^{{]+)\{{")
    for match in pattern.finditer(text):
        depth = 1
        cursor = match.end()
        while cursor < len(text) and depth:
            depth += (text[cursor] == "{") - (text[cursor] == "}")
            cursor += 1
        if depth != 0:
            raise SystemExit(f"Unbalanced nginx block after: {match.group(0)!r}")
        yield match.group(1).strip(), text[match.end(): cursor - 1]


http_server = next((body for header, body in blocks("server", source) if re.search(r"\blisten\s+80\s*;", body)), None)
if http_server is None:
    raise SystemExit("nginx.prod.conf: HTTP :80 server block not found")

required_locations = ["/.well-known/", "/connect/", "/auth/", "/api/users/"]
location_blocks = {header.split()[0]: body for header, body in blocks("location", http_server)}
guard = "if ($is_docker_internal = 0)"

missing = [location for location in required_locations if guard not in location_blocks.get(location, "")]
if missing:
    raise SystemExit(
        "nginx.prod.conf: plaintext auth proxy must be Docker-internal only; missing HTTPS guard in "
        + ", ".join(missing)
    )

if "https://$host$request_uri" in http_server:
    raise SystemExit(
        "nginx.prod.conf: HTTP redirects must use the canonical host, not the untrusted Host header"
    )

print("Nginx contracts valid: external HTTP auth traffic is redirected before proxying.")
PY
