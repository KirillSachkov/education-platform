"""Read the protected, single-line Compose environment without shell evaluation."""

import os
import json
import re
from pathlib import Path
import subprocess


def connection_database(value):
    """Read an ordinary database name without splitting quoted passwords."""
    if not isinstance(value, str) or len(value) > 65536:
        raise ValueError("bounded database connection string required")
    position, databases = 0, []
    while position < len(value):
        while position < len(value) and (value[position].isspace() or value[position] == ";"):
            position += 1
        if position == len(value):
            break
        start = position
        while position < len(value) and value[position] not in "=;":
            position += 1
        if position == len(value) or value[position] != "=":
            raise ValueError("invalid database connection field")
        key = value[start:position].strip().lower()
        if not key:
            raise ValueError("empty database connection field")
        position += 1
        while position < len(value) and value[position].isspace():
            position += 1
        parts = []
        if position < len(value) and value[position] in "\"'":
            quote = value[position]; position += 1
            while True:
                if position == len(value):
                    raise ValueError("unterminated database connection value")
                character = value[position]; position += 1
                if character == quote:
                    if position < len(value) and value[position] == quote:
                        parts.append(quote); position += 1
                    else:
                        break
                else:
                    parts.append(character)
            while position < len(value) and value[position].isspace():
                position += 1
            if position < len(value) and value[position] != ";":
                raise ValueError("invalid quoted database connection value")
            item = "".join(parts)
        else:
            start = position
            while position < len(value) and value[position] != ";":
                position += 1
            item = value[start:position].strip()
        if key in {"database", "initial catalog"}:
            databases.append(item)
    if len(databases) != 1 or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]{0,62}", databases[0]):
        raise ValueError("one explicit ordinary application database required")
    return databases[0]
import tempfile
import re
import stat


def private_environment(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "r") as stream:
        metadata = os.fstat(stream.fileno())
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_uid != os.geteuid() or metadata.st_mode & 0o077:
            raise ValueError("environment file ownership or permissions invalid")
        body = stream.read(1024 * 1024 + 1)
        after = os.fstat(stream.fileno())
        if (metadata.st_size, metadata.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
            raise ValueError("environment file changed while reading")
        if len(body) > 1024 * 1024 or not body.strip():
            raise ValueError("environment file empty or too large")
    dotenv(body)
    return body


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


def compose_environment(path, root, execute=None):
    """Decode actual values with the same Compose env_file rules as deployment."""
    keys = dotenv(private_environment(path))
    with tempfile.TemporaryDirectory(prefix="private-env-") as directory:
        probe = Path(directory) / "compose.json"
        descriptor = os.open(probe, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w") as stream:
            json.dump({"services": {"dotenv-values": {"image": "unused:dotenv-parser", "env_file": [str(path.resolve())]}}}, stream)
        arguments = ["docker", "compose", "--project-directory", str(root), "-p", "private-environment", "-f", str(probe), "config", "--format", "json"]
        if execute is None:
            try:
                result = subprocess.run(arguments, capture_output=True, timeout=180)
            except subprocess.SubprocessError as error:
                raise ValueError("private Compose environment resolution failed") from error
            if result.returncode:
                raise ValueError("private Compose environment resolution failed")
            body = result.stdout
        else:
            body = execute(arguments)
    values = json.loads(body)["services"]["dotenv-values"]["environment"]
    if set(values) != set(keys) or any(not isinstance(value, str) for value in values.values()):
        raise ValueError("canonical Compose environment inventory mismatch")
    # Compose config escapes every literal dollar for re-reading the model.
    # Decode that serialization once before passing credentials to a process.
    # https://github.com/docker/compose/blob/main/cmd/compose/config.go
    return {key: value.replace("$$", "$") for key, value in values.items()}
