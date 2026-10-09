# Build and CI

Clone the public source with its public submodule:

```bash
git clone --recurse-submodules https://github.com/KirillSachkov/education-platform.git
cd education-platform
cp backend/nuget.config.example backend/nuget.config
dotnet build backend/backend.slnx
cd frontend
npm ci
npm run lint
npm test
npm run build
```

Use the .NET SDK selected by root `global.json` and Node.js 22.
NuGet and npm use public feeds. These commands require no registry credentials.
See [the public-source boundary](public-source.md) for demonstration seeds and runtime assets.

## Pull requests

Every PR to `main` creates `required-checks`. The workflow has no path filter.
`scripts/ci/github-ci.py` compares the immutable event PR head against its real
merge-base with the event base SHA. It includes deleted paths and both sides of
renames. The jobs test GitHub's merge commit. Missing commits, malformed SHAs and
an inconsistent checkout fail selection.

`scripts/ci/github-ci-paths.json` declares affected paths. A selected job must pass;
failed or cancelled jobs block the aggregate. Only a deterministically unselected
job may be skipped. Whitespace and selection always run.

| Check | Selected scope and behavior |
| --- | --- |
| `backend` | Backend, SDK, submodule and CI: full solution build plus nine unit targets |
| `frontend` | Frontend, its ratchets and CI: typecheck, ESLint and strict-TS ratchets, explicit-base Prettier, build, Vitest; Knip warns |
| `admin` | Admin MCP, audit wrapper and CI: typecheck, tests, bounded public npm audit retries |
| `migrations` | Backend and CI: immutable schema/data migrations; new corrective migrations and snapshot updates remain allowed |
| `compose` | Compose/nginx, operational contracts and CI: configuration and fake-command tests |
| `images` | Image source and CI: application ports, healthchecks, cross-service restore context, source labels and notice packaging |
| `harness` | Instructions, harness, templates and CI: instruction/provenance validator |
| `templates` | Templates, shared dependencies, SDK and CI: generate, restore and build both service templates |
| `rules` | Project references and CI: selector/aggregate/digest tests, test-rule coverage and architecture contracts |

Fork PRs receive `contents: read`, no package write permission or deployment
credentials. Checkout does not persist credentials. The workflows use
`pull_request`, never `pull_request_target`.

The optional **Integration tests** workflow runs all 12 service suites and the
complete Shared ContentAccess suite, including Redis. Run it with:

```bash
gh workflow run integration.yml --repo KirillSachkov/education-platform --ref <branch>
```

Tests use the hosted runner's native Docker and public upstream images. There is
no DinD mirror, registry login, Ryuk disable or private image prefix. FileService
builds its job-local MinIO image from unmodified, pinned public source because the
upstream container repository is unavailable. The source archive checksum and
builder/runtime image digests are fixed in `scripts/ci/minio-test.Dockerfile`.
MinIO retains its AGPL license; this test image is neither published nor deployed.

Before local FileService integration tests, build the same fixture:

```bash
docker build -f scripts/ci/minio-test.Dockerfile -t minio/minio:latest scripts/ci
```

Acquire the [local runtime lease](agents/local-runtime.md) before this Docker build.
The locally cached tag satisfies Testcontainers' default missing-image pull policy.
This fixture tag is independent of immutable application release images. Optional
CI execution does not replace the local integration evidence required for a change.

## Main images

A trusted push to `KirillSachkov/education-platform` `main` runs all mandatory
classes. A zero-parent initial push with all-zero `before` uses the empty Git tree
as its diff base, selects every class and builds all 13 application images.
It never uses `HEAD~1`, `origin/dev` or a missing-base formatting skip.

Only that verified initial push may retain byte-identical formatting debt listed
in `scripts/ci/github-bootstrap-prettier-baseline.json` and whitespace debt listed
in `scripts/ci/github-bootstrap-whitespace-baseline.json`. The selector must confirm
all-zero `before`, trusted public `main` and an actual zero-parent commit. The
formatter independently checks those guards and the empty-tree base. Per-file
SHA-256 hashes identify unchanged historical inputs; no private history identifier
appears in baseline provenance. Changed debt files and every new input must pass
Prettier and whitespace checks. Normal PRs and later main pushes never use this
exemption. Each job summary reports its bounded number of retained debt files.

After `required-checks` succeeds, four standard `ubuntu-24.04` jobs at most build
images concurrently. Each image receives the full source SHA as tag, revision and
version. There are no `latest` or stale-tag promotion paths:

```text
ghcr.io/kirillsachkov/education-platform/<service>:<40-character-GitHub-SHA>
```

Publishing has `packages: write` only in the trusted image job. It has no production
environment, deployment step or legacy-package credential. Public package visibility
is a separate repository-owner setting; a successful push does not prove it.

The final `image-manifest-<SHA>` artifact requires exactly 13 records from the same
SHA. Each contains an immutable digest reference and full-SHA tag. A missing,
duplicate, malformed or stale record fails collection. Deployment consumers must
use this manifest, rather than infer completeness from a successful matrix member.

Canonical source notices enter builds through the `image_notices` BuildKit file
secret. Backend images retain restored package metadata and notice files.
Frontend images retain installed npm metadata, license files and README notices,
including the libvips dependency notice. They also retain the
[complete native libvips bundle](../frontend/third-party/sharp-libvips-1.3.4/README.md)
under `/app/licenses/native-libvips/`: GNU texts, component copyrights,
exact versions and matching-source download directions. The collector verifies
all supplement checksums and rejects mismatched installed native versions.
`npm test` covers missing licenses, changed copyrights and dependency upgrades.
FileService also includes the three
canonical SkiaSharp notice files directly under `/app/licenses/`.
Image acceptance must inspect final filesystem contents and applicable binary
distribution terms. Source checks alone do not certify image-level licensing.

Only digest JSON files are uploaded: matrix artifacts last one day and the complete
manifest lasts seven days. Build records, test bundles and persistent caches are
disabled. Diagnostics remain in logs and the job summary. Standard public hosted
runners are [free](https://docs.github.com/en/billing/concepts/product-billing/github-actions);
artifact storage still has a quota.

Production script tests exercise the manual GitHub transport and host adapter. Migration,
rollback, environment-export and restore checks use their actual implementations; no legacy
pipeline definition is required. Real hosted deploy and rollback remain separate external evidence.

## External acceptance after publication

These checks require the actual public repository. Local selection tests and the
local script fixtures do not provide this evidence.

1. Open a PR that changes only a contributor document. Read selection output and
   confirm all unselected checks are skipped, whitespace passes, and
   `required-checks` succeeds. Read the check run and confirm Actions app `15368`.
2. Open a PR containing `frontend/src/__tests__/ci-gate-probe.test.ts` with a Vitest
   test below. Confirm `frontend` and `required-checks`
   fail. Confirm branch protection rejects its merge. Remove the probe and verify
   the passing gate on the new revision. Perform the same probe from a fork and
   read the effective job permissions; no credentials are supplied.
   Use this complete probe file:

   ```typescript
   import { expect, test } from "vitest";

   test("required gate rejects a failing selected suite", () => {
     expect(true).toBe(false);
   });
   ```

3. Read main branch protection: strict `required-checks` bound to app `15368`,
   administrators enforced, PRs required with zero human approvals, no force push
   or deletion. Confirm the first zero-parent main run selects every class.
4. On the exact trusted main run, confirm all 13 image jobs and the final manifest
   pass. Download its bounded manifest and inspect it:

   ```bash
   gh run download <main-run-id> --repo KirillSachkov/education-platform \
     --name image-manifest-<GitHub-SHA> --dir /tmp/image-acceptance
   jq -e --arg sha '<GitHub-SHA>' \
     '.source_sha == $sha and (.images | length == 13) and
      ([.images[].name] | unique | length == 13) and
      all(.images[]; .source_sha == $sha and (.digest | test("^sha256:[0-9a-f]{64}$")))' \
     /tmp/image-acceptance/image-manifest.json
   ```

5. Read every new GHCR package's visibility and digest metadata with the owner API.
   Confirm all 13 are public. Independently pull each digest using an empty Docker
   credential directory:

   ```bash
   docker_config="$(mktemp -d)"
   jq -r '.images[].reference' /tmp/image-acceptance/image-manifest.json |
     while IFS= read -r reference; do
       DOCKER_CONFIG="$docker_config" docker pull "$reference" || exit 1
     done
   rm -rf "$docker_config"
   ```

   Inspect each image's source/revision label, notices and safe seed inputs. Verify
   FileService's SkiaSharp files byte-for-byte against canonical notices. Check
   frontend native dependency notices and binary distribution terms separately.
   Do not grant this public repository access to private legacy packages.
6. If optional integration evidence is requested, dispatch `integration.yml` at
   the same source SHA and read all 12 suites plus ContentAccess/Redis results.
   Record this separately from mandatory PR acceptance and local affected tests.

Workflow syntax and token behavior follow [GitHub's documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
The OCI source label follows [GHCR guidance](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).
Actions are pinned to immutable official repository commits; version comments
identify the release used for each pin.
