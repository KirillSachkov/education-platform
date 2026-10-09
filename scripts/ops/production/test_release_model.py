import copy
import json
from pathlib import Path
import unittest

from release_model import (
    REPOSITORY, PUBLIC_REGISTRY, LEGACY_REGISTRY, SERVICES, HEALTH_SERVICES, LEGACY_SERVICES, services_for_registry,
    dispatch_inputs, image_manifest, private_roles, strict_json, trusted_build_run, trusted_dispatch,
)

SHA = "a" * 40


def manifest(sha=SHA, registry=PUBLIC_REGISTRY):
    digest = "sha256:" + "b" * 64
    return {"schema_version": 1, "source_sha": sha, "images": [
        {"name": name, "source_sha": sha, "tag": registry + "/" + name + ":" + sha,
         "digest": digest, "reference": registry + "/" + name + "@" + digest}
        for name in services_for_registry(registry)
    ]}


def roles():
    result = {"schema_version": 1, "roles": {},
              "postgres_image": LEGACY_REGISTRY + "/postgres-pgvector@sha256:" + "c" * 64,
              "config_hashes": {"docker-compose.prod.yml": "d" * 64}}
    for name, sha in [("current", SHA), ("previous", "e" * 40)]:
        result["roles"][name] = {"role": name, "source_sha": sha, "image_manifest": manifest(sha, LEGACY_REGISTRY),
                                 "metadata": {"IMAGE_TAG": sha, "RELEASE_COMMIT_SHA": sha,
                                              "DOCKER_REGISTRY": LEGACY_REGISTRY + "/", "RELEASE_PIPELINE_ID": "12", "MEDIA_BINDING_PROTOCOL": "1"}}
    return result


class ProductionReleaseInputs(unittest.TestCase):
    def test_inventory_matches_build_and_exact_health_scope(self):
        root = Path(__file__).resolve().parents[3]
        declared = json.loads((root / "scripts/ci/github-ci-paths.json").read_text())["images"]
        self.assertEqual(set(SERVICES), {row["name"] for row in declared})
        self.assertEqual(len(HEALTH_SERVICES), 9)
        self.assertNotIn("telegram-bot-service", HEALTH_SERVICES)

    def test_known_manual_input_combinations(self):
        for operation, release in [("probe", "current"), ("deploy", "current"), ("rollback", "previous"), ("rollback", "recorded-previous")]:
            self.assertEqual(dispatch_inputs(operation, release)["operation"], operation)
        self.assertEqual(dispatch_inputs("deploy", "normal-public-build", SHA, "123")["source_sha"], SHA)

    def test_private_identity_and_implicit_latest_are_rejected_before_transport(self):
        for inputs in [("deploy", "latest", "", ""), ("deploy", "previous", "", ""),
                       ("rollback", "normal-public-build", SHA, "123"), ("probe", "current", SHA, ""),
                       ("deploy", "current", SHA, "123"), ("deploy", "normal-public-build", "0" * 40, "123"),
                       ("deploy", "normal-public-build", SHA, "$(id)")]:
            with self.subTest(inputs=inputs), self.assertRaises(ValueError):
                dispatch_inputs(*inputs)

    def test_only_trusted_main_manual_event_receives_production_transport(self):
        env = {"GITHUB_REPOSITORY": REPOSITORY, "GITHUB_REF": "refs/heads/main", "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_SHA": SHA}
        trusted_dispatch(env)
        for key, value in [("GITHUB_REPOSITORY", "fork/platform"), ("GITHUB_REF", "refs/heads/feature"),
                           ("GITHUB_EVENT_NAME", "pull_request"), ("GITHUB_SHA", "short")]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                trusted_dispatch({**env, key: value})

    def test_complete_canonical_public_and_private_manifests(self):
        self.assertEqual(len(image_manifest(manifest(), SHA)), 10)
        self.assertEqual(len(image_manifest(manifest(registry=LEGACY_REGISTRY), SHA, LEGACY_REGISTRY)), 14)

    def test_source_and_frozen_legacy_topologies_cannot_be_interchanged(self):
        self.assertIn("trainer-service", LEGACY_SERVICES)
        self.assertNotIn("trainer-service", SERVICES)
        for retired in ("search-service", "tag-service"):
            self.assertIn(retired, LEGACY_SERVICES)
            self.assertNotIn(retired, SERVICES)
        with self.assertRaises(ValueError):
            image_manifest(manifest(registry=LEGACY_REGISTRY), SHA)
        legacy = manifest(registry=LEGACY_REGISTRY)
        legacy["images"] = [row for row in legacy["images"] if row["name"] != "trainer-service"]
        with self.assertRaises(ValueError):
            image_manifest(legacy, SHA, LEGACY_REGISTRY)
        public = manifest()
        extra = manifest()["images"][0].copy()
        extra.update(name="trainer-service", tag=PUBLIC_REGISTRY + "/trainer-service:" + SHA,
                     reference=PUBLIC_REGISTRY + "/trainer-service@" + extra["digest"])
        public["images"].append(extra)
        with self.assertRaises(ValueError):
            image_manifest(public, SHA)

    def test_missing_duplicate_stale_and_arbitrary_registry_refs_fail(self):
        cases = []
        missing = manifest(); missing["images"].pop(); cases.append(missing)
        duplicate = manifest(); duplicate["images"][-1] = copy.deepcopy(duplicate["images"][0]); cases.append(duplicate)
        stale = manifest(); stale["images"][0]["source_sha"] = "e" * 40; cases.append(stale)
        tag = manifest(); tag["images"][0]["reference"] = PUBLIC_REGISTRY + "/auth-service:latest"; cases.append(tag)
        foreign = manifest(); foreign["images"][0]["reference"] = "example.com/auth-service@sha256:" + "b" * 64; cases.append(foreign)
        extra = manifest(); extra["images"][0]["unexpected"] = True; cases.append(extra)
        for value in cases:
            with self.subTest(value=value), self.assertRaises(ValueError):
                image_manifest(value, SHA)

    def test_approved_roles_remain_separate_from_rotating_release_metadata(self):
        private_roles(roles())
        for change in ["missing-role", "wrong-protocol", "wrong-registry", "config-traversal", "wrong-postgres", "multiline"]:
            value = roles()
            if change == "missing-role": del value["roles"]["previous"]
            if change == "wrong-protocol": value["roles"]["previous"]["metadata"]["MEDIA_BINDING_PROTOCOL"] = "0"
            if change == "wrong-registry": value["roles"]["current"]["metadata"]["DOCKER_REGISTRY"] = "example.com/"
            if change == "config-traversal": value["config_hashes"]["docker/../../etc/passwd"] = "d" * 64
            if change == "wrong-postgres": value["postgres_image"] = "postgres:16"
            if change == "multiline": value["roles"]["current"]["metadata"]["RELEASED_AT"] = "date\nINJECTED=1"
            with self.subTest(change=change), self.assertRaises(ValueError):
                private_roles(value)

    def test_image_build_requires_exact_successful_trusted_main_run(self):
        run = {"id": 123, "head_sha": SHA, "event": "push", "head_branch": "main", "path": ".github/workflows/ci.yml",
               "status": "completed", "conclusion": "success", "repository": {"full_name": REPOSITORY}, "head_repository": {"full_name": REPOSITORY}}
        trusted_build_run(run, SHA, "123")
        for key, value in [("id", 124), ("head_sha", "e" * 40), ("event", "pull_request"), ("head_branch", "feature"),
                           ("path", ".github/workflows/other.yml"), ("status", "in_progress"), ("conclusion", "failure"),
                           ("repository", {"full_name": "fork/platform"}), ("head_repository", {"full_name": "fork/platform"})]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                trusted_build_run({**run, key: value}, SHA, "123")

    def test_duplicate_or_nonfinite_json_is_not_a_release_manifest(self):
        for text in ['{"source_sha":"one","source_sha":"two"}', '{"value":NaN}', '{"value":Infinity}']:
            with self.subTest(text=text), self.assertRaises(ValueError):
                strict_json(text)


if __name__ == "__main__":
    unittest.main()
