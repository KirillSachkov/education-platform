# Prepare a private runtime release

This procedure prepares evidence for a future release. It does not authorize
deployment, select publication dates, install secrets or send notifications.
The owner-approved private release packet selects those inputs separately.

## Preserve and compare the inputs

Keep the packet outside the public checkout with directories mode `0700` and
files mode `0600`. Retain the original approved Markdown, PDF and brand assets,
their provenance, SHA256 hashes and encrypted recovery receipt. Keep current
runtime files separate from future drafts. An old source date is provenance,
not a future publication instruction.

Compare both version registries from each exact source identity:

- `frontend/src/shared/legal/versions.ts`
- `backend/AuthService/src/AuthService.Core/Services/LegalDocumentVersions.cs`

Record current runtime versions, candidate versions and every difference.
Verify all current and archived Markdown/PDF pairs against the private manifest.
Preserve old filenames and bytes. Check the PDF text against its Markdown and
inspect its pages. A PDF signature alone does not establish text consistency.

Unfinished dates belong only in an explicitly marked private draft. After the
owner selects dates, reproduce the dated documents from that draft, regenerate
the changed PDFs and review their complete text and layout. Preserve unchanged
archive pairs byte-for-byte. Record a new manifest for the final packet.

## Validate readiness without production access

Run the host-adapter tests from the repository root:

```bash
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s scripts/ops/production -p 'test_*.py'
```

`NormalRuntime` tests use synthetic files and a fake host command runner. They
exercise exact source and registry selection, matching registry versions,
current pairs, archived-file hashes, unfinished text, PDF signatures, business
schema and UID1000 read checks. They create no production approval and make no
SSH, Docker or notification calls.

Run the existing asset validator against the completed private packet from
`frontend/`, using absolute local paths:

```bash
LEGAL_DOCUMENTS_DIR="$PRIVATE_MARKDOWN_DIR" \
LEGAL_PDFS_DIR="$PRIVATE_PDF_DIR" \
node --experimental-strip-types scripts/validate-legal-assets.mjs
```

This validates the source registry's current pairs. It does not select dates,
check every archive, compare PDF text or approve the legal terms.

## Prepare the future operation

The private packet must identify the exact trusted main source SHA, successful
build run and image manifest. Its `NORMAL_RUNTIME_APPROVAL` draft must describe
schema version `1`, the same `source_sha`, both `registry_hashes`, the three
runtime `environment` paths and every private `file_hashes` entry. Use the
schema and host rules in [github-production.md](github-production.md#private-runtime-for-normal-source).
Preparation does not install that draft as an environment secret.

Before an authorized deploy, validate actual UID1000 readability and parent
directory traversal on the host. Local tests simulate that command; they do not
prove host permissions. The business input must match the approved eight-field
schema. Mount the completed inputs read-only. Legacy images keep their own
embedded files and immutable release roles.

Include a private notification draft, publication sequence, recipient checks,
administrative before/after record and preservation of earlier purchased
access. Record the applicable notice interval and accepted dates privately.
Do not treat source merge, image build or document validation as notification
delivery or permission to change runtime data.

Use the existing [release workflow](agents/release-pipelines.md) and
[recovery procedure](github-production.md#rollback-and-recovery) when separately
authorized. The packet must name the backup object/version and restore evidence,
current and rollback manifests, configuration, migration compatibility and
changed-path probes. After startup, check every current and archived legal URL,
download every PDF and compare its bytes. Check the approved footer fields and
contact links. These archive and displayed-data checks supplement the adapter's
automatic current-document smoke checks.

Retain failed-operation evidence. A rollback needs its own owner command;
publication and sent messages require separate recovery decisions.
