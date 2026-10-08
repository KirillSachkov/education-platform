"""Tag exactly a healthy normal release; this job receives no production secrets."""

import json
import os
import re
import urllib.error
import urllib.request

from release_model import REPOSITORY, full_sha, trusted_dispatch


def main():
    trusted_dispatch(os.environ)
    sha = full_sha(os.environ["DEPLOYED_SOURCE_SHA"])
    version = os.environ["DEPLOYED_VERSION"]
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("valid deployed semantic version required")
    api = "https://api.github.com/repos/" + REPOSITORY
    headers = {"Authorization": "Bearer " + os.environ["GH_TOKEN"],
               "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2026-03-10"}
    reference = "tags/v" + version
    try:
        with urllib.request.urlopen(urllib.request.Request(api + "/git/ref/" + reference, headers=headers), timeout=30) as response:
            existing = json.load(response)
        if existing["object"]["type"] != "commit" or existing["object"]["sha"] != sha:
            raise ValueError("existing release tag identifies a different commit")
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        body = json.dumps({"ref": "refs/" + reference, "sha": sha}).encode()
        with urllib.request.urlopen(urllib.request.Request(api + "/git/refs", data=body, headers=headers, method="POST"), timeout=30) as response:
            created = json.load(response)
        if created["object"]["sha"] != sha:
            raise ValueError("created release tag identity mismatch")
    print("Verified deployed release tag v" + version + ".")


if __name__ == "__main__":
    main()
