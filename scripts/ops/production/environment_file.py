"""Read the protected, single-line Compose environment without shell evaluation."""

import os
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

