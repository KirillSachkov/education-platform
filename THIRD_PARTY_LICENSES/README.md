# License inventory

The root [MIT license](../LICENSE) covers the platform's original source code,
developer documentation and demonstration data. Bundled third-party content retains
the licenses below. Product branding, the author portrait and legal content are
covered separately by [the asset notice](../ASSET_NOTICE.md).

## Agent skills

The imported content is the unchanged `inside-engineering` 0.3.11 snapshot pinned in
[product-harness.json](../.inside-harness/product-harness.json) to
`sachkov-inside/workspace@3c970cdd2fb89affe7d8aa31197c210294260568`.
The imported Matt Pocock suite comes from `mattpocock/skills` v1.2.3,
commit `885e2ca4d842d139e9aef4e48d366c63cb1b8013`.
Inside adapted its delivery routing, review closure and ADR lifecycle guidance.
The other skills came through the Inside landing snapshot; the upstream refs in
[provenance.json](provenance.json) identify reviewed license evidence, rather than
asserting an undocumented import version.

| Content under `.inside-harness/skills/` | License and attribution |
| --- | --- |
| Matt Pocock's 25 skills: all `managedSkills` except the seven rows below | [MIT](matt-pocock-MIT.txt), Copyright (c) 2026 Matt Pocock; [upstream](https://github.com/mattpocock/skills/tree/885e2ca4d842d139e9aef4e48d366c63cb1b8013) |
| `frontend-design` | [Existing Apache-2.0 notice](../.inside-harness/skills/frontend-design/LICENSE.txt); [Anthropic skills](https://github.com/anthropics/skills/tree/main/skills/frontend-design) |
| `impeccable` | [Apache-2.0](impeccable-Apache-2.0.txt), Copyright 2025 Paul Bakaus; retain in-file notices and [upstream NOTICE](impeccable-NOTICE.md). Its `reference/ios.md` and `android.md` derive from ehmo's platform-design-skills under [MIT](ehmo-platform-design-skills-MIT.txt). |
| `modern-web-guidance` software | [Apache-2.0](modern-web-guidance-Apache-2.0.txt); [Modern Web Guidance contributors](https://github.com/GoogleChrome/modern-web-guidance-src) |
| `modern-web-guidance/guides/**` documentation | [CC BY 4.0](CC-BY-4.0.txt); Modern Web Guidance contributors. [Upstream attribution](https://github.com/GoogleChrome/modern-web-guidance-src/blob/271a5501f46e70c8904ea134ab334a05c8dc992f/README.md) credits Mozilla Contributors/MDN and W3C, WHATWG and IETF specifications. The guides remain unchanged in this snapshot. |
| `playwright-cli` | [Apache-2.0](playwright-cli-Apache-2.0.txt), Microsoft Corporation; [upstream](https://github.com/microsoft/playwright-cli) |
| `vercel-react-best-practices` and `web-design-guidelines` | [MIT terms](MIT-terms.txt), declared by [Vercel's README](https://github.com/vercel-labs/agent-skills/blob/063bee94c3f4df8453406c830b0a7df0f2860278/README.md). Attribution: Vercel / Vercel Engineering. Upstream provides no standalone copyright notice. |
| `karpathy-guidelines` | [MIT terms](MIT-terms.txt), declared in the bundled skill and [source README](https://github.com/multica-ai/andrej-karpathy-skills/blob/2c606141936f1eeef17fa3043a72095b4765b9c2/README.md). Derived from [Andrej Karpathy's observations](https://x.com/karpathy/status/2015883857489522876). The skill matches the source Git blob `6a62d0441753157ca6ca50479e490c2948033adb`; upstream supplies no separate copyright notice. |

The Modern Web Guidance documentation row is a scope within its software row.
Together the table identifies the seven additional skills. Exact upstream license
texts are preserved, including ehmo's supplied `Copyright (c) 2026` line without
an asserted holder. The common MIT terms do not invent missing copyright metadata.
Local operational skills retain the platform's MIT license.

## Application source and assets

| Content | License and evidence |
| --- | --- |
| shadcn-derived `frontend/src/shared/ui/kit/` | [MIT](shadcn-ui-MIT.txt), Copyright (c) 2023 shadcn; identified by `frontend/components.json` and `docs/agents/frontend-fsd.md`. The original import commit was not retained. |
| `frontend/public/fonts/PTSans-{Regular,Bold}.ttf` | [SIL OFL 1.1](../frontend/public/fonts/OFL.txt), extracted unchanged from each font's name table. Copyright 2010 ParaType Ltd.; the separate copyright metadata also identifies 2009 ParaType. Reserved names: PT Sans, PT Serif, ParaType. |
| `backend/external/telegram-bot-flow` | [MIT declaration](https://github.com/KirillSachkov/telegram-bot-flow/blob/0430cd4815271bc9960090a4c37190926a75c00f/README.md) at pinned commit `0430cd4815271bc9960090a4c37190926a75c00f`; [MIT terms](MIT-terms.txt). Attribution: KirillSachkov/telegram-bot-flow. This public submodule supplies no separate copyright notice. |
| `backend/docker/certs/mincifry/*.crt` | Public trust certificates; their download source and hashes are documented in the adjacent README. These are trust anchors, rather than private keys. |
| `backend/FileService/tests/FileService.IntegrationTests/Resources/test-file.mp4` | Original synthetic fixture under root MIT; generation command is recorded in [public-source.md](../docs/public-source.md). |

## Restored dependencies and built artifacts

NuGet and npm dependencies are restored from public package feeds. Their own
licenses remain authoritative; root MIT does not relicense them. Package versions
are recorded in `backend/Directory.Packages.props`, the public submodule and npm
lockfiles. Preserve dependency notices when distributing compiled artifacts.

FileService uses SkiaSharp 4.153.1 and matching native packages from NuGet.org.
The managed binding retains [MIT](SkiaSharp-MIT.txt), with Xamarin and Microsoft
copyright notices. Linux uses `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.153.1.
Its [license](SkiaSharp-NativeAssets-MIT.txt) and complete
[native dependency notices](SkiaSharp-NativeAssets-THIRD-PARTY-NOTICES.txt) retain
third-party terms beyond the binding's MIT license.

SkiaSharp also restores macOS and Win32 native assets for .NET 10. Their supplied
license and notice files are byte-identical to the Linux files in this release.
[Provenance](provenance.json) records all four package origins, source commits and hashes.
The texts are copied unchanged from signed packages; no native binaries are vendored.
Keep these three files alongside distributed FileService binaries under `licenses/`:
`SkiaSharp-MIT.txt`, `SkiaSharp-NativeAssets-MIT.txt`, and
`SkiaSharp-NativeAssets-THIRD-PARTY-NOTICES.txt`. Check publish output and the final
container; the default NuGet publish does not include these notices.

The [upstream deployment guide](https://github.com/mono/SkiaSharp/blob/v4.153.1/documentation/dev/packages.md)
places Linux native assets in the executable project. `NoDependencies` excludes
fontconfig integration; it remains a native library with operating-system dependencies.
The package name does not make the native dependency licenses MIT.

npm includes LGPL-3.0-or-later libvips and MPL-2.0 packages. Image publishers must
retain their package notices and satisfy applicable artifact distribution terms.
Source readiness does not certify an image-level license bundle.

The original product graphics, portrait and legal PDFs/Markdown are excluded.
Neutral geometric replacement graphics are original work under root MIT; see
[the asset boundary](../ASSET_NOTICE.md). The public tree includes no proprietary
font programs extracted from the private legal PDFs.
