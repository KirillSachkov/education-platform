# Public source boundary

The source tree contains platform code, developer documentation and safe examples.
Private course content, student testimonials, historical task/review documents,
credentials and operator-specific deployment addresses stay outside this tree.
The original repository history remains private; publication uses a separate clean
initial snapshot. Deleting a file from this branch does not sanitize its history.

## Demonstration data

The embedded EducationContentService JSON resource contains small original examples
for the level test, including section scoring and thresholds. These examples support
local development rather than professional assessment.

The explicit `seed-level-test` CLI command loads this resource. Application startup
does not replace persisted questions. The seeder preserves an existing quiz unless
an operator supplies `--force`. Private content stays in the database and authoring APIs.
The landing page uses clearly marked fictional testimonials with no personal handles.

The FileService video fixture is generated from a solid color and silence:

```bash
ffmpeg -f lavfi -i 'color=c=blue:s=1280x720:r=30' \
  -f lavfi -i 'anullsrc=r=48000:cl=stereo' -t 30.5 \
  -c:v libx264 -pix_fmt yuv420p -c:a aac -map_metadata -1 \
  -movflags +faststart test-file.mp4
```

## Configuration and build

`backend/nuget.config.example` and `backend/nuget.config.ci` use only nuget.org.
The TelegramBotFlow submodule is public on GitHub. A developer can restore and
build source without a private package feed or registry image.

FileService image variants use SkiaSharp 4.153.1 and matching Linux native assets
from NuGet.org. The binding uses MIT and requires no private license key.
Native components retain their own licenses. The three SkiaSharp license and notice
files in `THIRD_PARTY_LICENSES/` must accompany FileService publish output under
`licenses/`, including inside the final container. Verify those files in the
artifact; ordinary NuGet publishing does not copy them automatically.

Deployment configuration and release images are separate from source compilation.
Operator-specific SSH destinations are provided through environment variables;
the Grafana wrapper requires `PROD_SSH_HOST`. Public product domains, public DNS
resolvers, Docker network ranges and certificate trust anchors are retained where
they are functional configuration or documented examples.

The historical review reports stay in the private archive. CI instead verifies
current service image ports and restore contexts with
`scripts/ci/validate-service-image-contracts.py`; normal build, test and instruction
checks remain authoritative for current source.

See [the license inventory](../THIRD_PARTY_LICENSES/README.md) for bundled licenses
and [the asset notice](../ASSET_NOTICE.md) for product assets.

## Private runtime assets

Original product graphics and the portrait are replaced with neutral, original
geometric illustrations. Run `node scripts/generate-public-assets.mjs` from
`frontend/` after `npm ci` to regenerate them. Existing static URLs, image sizes
and PWA icon entries remain usable; the avatar is decorative and does not claim
to be an author photograph. Original graphic files are absent from the public tree.

Legal Markdown and PDFs are excluded rather than replaced with fictional terms.
The current versions and `/legal/<slug>`, `/legal/<slug>/vN` and
`/legal-docs/<slug>-vN.pdf` URLs are unchanged. Both current and archived documents
are read at request time. Missing files return 404; a clean source checkout does
not contain enforceable legal terms. Do not deploy it for registrations or sales
until the owner supplies the unchanged private documents and PDFs, including all
archived versions. Verify each legal page and PDF before traffic reaches the image.

The server-only paths, version workflow and mandatory operator check are defined in
[the legal runtime reference](legal.md#приватные-файлы-в-runtime).

The Docker build excludes both directories; the standalone image creates empty
mount destinations and does not embed private documents. The coordinator supplies
read-only mounts when configuring the production runtime. For example, add these
options to the existing frontend container command, preserving its other options:

```bash
--mount type=bind,src=/srv/education-platform/private-legal/markdown,dst=/run/legal/markdown,readonly \
--mount type=bind,src=/srv/education-platform/private-legal/pdf,dst=/run/legal/pdf,readonly \
--env LEGAL_DOCUMENTS_DIR=/run/legal/markdown \
--env LEGAL_PDFS_DIR=/run/legal/pdf
```

For local `npm run start`, set the same variables to absolute host directories.
The PDF generator also accepts these variables. Keep private files outside the
public checkout; Git and Docker ignore the legacy local directories as protection
against accidental inclusion. Archiving and deployment configuration are owned by
the migration coordinator, not this source-cleanup MR.

## Private business footer input

Real proprietor registration identifiers, address and contact details are excluded
from source. Set server-only `BUSINESS_DETAILS_FILE` to an absolute runtime JSON
path. `frontend/business-details.example.json` describes the required fields using
fictional data. The application validates this exact schema and displays only
those public footer fields; the runtime path and other configuration stay server-side.
Next.js reads the file per request, so a public source build embeds no real details.

Keep the actual file outside the checkout and mount it read-only, for example at
`/run/legal/business-details.json`. The container's `node` user needs read access.
The local `frontend/business-details.json` filename is ignored by Git and Docker.
Absent input omits business details; malformed input or a permission failure is an
error. Absence is suitable for a source demo, but does not establish production
readiness. Current legal document pages use the same runtime email for archive requests.
Without a configured business record, they omit the email request sentence.

Before release, the coordinator must provide and verify the currently
approved business details in both footer variants, including their contact link,
while preserving the approved legal documents and registries.
