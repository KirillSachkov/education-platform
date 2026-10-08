import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { ImageResponse } from "next/og";

/**
 * Dynamic OpenGraph card generator.
 *
 * `GET /og?title=…&subtitle=…` renders a 1200×630 branded card used as the
 * `og:image` fallback for entities that have no cover of their own (see
 * `shared/seo/metadata-builders.ts`). Entities WITH a real cover keep using it;
 * this only fills the gap so a cover-less material/collection still shares as a
 * titled card instead of the generic static `/og-image.png`.
 *
 * Runtime is nodejs (not edge) so we can read the bundled Cyrillic font from
 * `public/fonts/` — `output: "standalone"` copies `public/` into the runner and
 * `process.cwd()` is the app root there (see frontend/Dockerfile).
 */
export const runtime = "nodejs";
// force-dynamic: render per request (read fonts + query params at request time),
// never statically prerender at build. The explicit Cache-Control below still
// makes the response cacheable — the query string fully determines the output.
export const dynamic = "force-dynamic";

const WIDTH = 1200;
const HEIGHT = 630;
const SITE_NAME = "SachkovLearn";
const DOMAIN = "sachkov-learn.net";

// Shrink the title as it gets longer so it always fits the card.
function titleFontSize(length: number): number {
  if (length <= 28) return 78;
  if (length <= 52) return 62;
  if (length <= 84) return 50;
  return 40;
}

function clamp(value: string, max: number): string {
  const trimmed = value.trim();
  return trimmed.length > max ? `${trimmed.slice(0, max - 1).trimEnd()}…` : trimmed;
}

export async function GET(request: Request): Promise<Response> {
  try {
    const { searchParams } = new URL(request.url);
    const title = clamp(searchParams.get("title") ?? "", 120) || SITE_NAME;
    const subtitle = clamp(searchParams.get("subtitle") ?? "", 96);

    const [bold, regular] = await Promise.all([
      readFile(join(process.cwd(), "public/fonts/PTSans-Bold.ttf")),
      readFile(join(process.cwd(), "public/fonts/PTSans-Regular.ttf")),
    ]);

    return new ImageResponse(
      (
        <div
          style={{
            height: "100%",
            width: "100%",
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-between",
            backgroundColor: "#0a0e1a",
            backgroundImage:
              "radial-gradient(circle at 18% -10%, rgba(45,212,191,0.20), transparent 45%), radial-gradient(circle at 110% 120%, rgba(59,130,246,0.16), transparent 42%)",
            padding: "72px 80px",
            fontFamily: "PT Sans",
          }}
        >
          {/* Wordmark */}
          <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
            <div style={{ width: 14, height: 46, borderRadius: 7, backgroundColor: "#2dd4bf" }} />
            <div
              style={{ fontSize: 36, fontWeight: 700, color: "#e6edf3", letterSpacing: -0.5 }}
            >
              {SITE_NAME}
            </div>
          </div>

          {/* Title + subtitle */}
          <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
            <div
              style={{
                display: "flex",
                fontSize: titleFontSize(title.length),
                fontWeight: 700,
                color: "#f8fafc",
                lineHeight: 1.12,
                letterSpacing: -1,
              }}
            >
              {title}
            </div>
            {subtitle ? (
              <div style={{ display: "flex", fontSize: 32, color: "#94a3b8", lineHeight: 1.3 }}>
                {subtitle}
              </div>
            ) : null}
          </div>

          {/* Domain footer */}
          <div style={{ display: "flex", fontSize: 26, color: "#64748b" }}>{DOMAIN}</div>
        </div>
      ),
      {
        width: WIDTH,
        height: HEIGHT,
        fonts: [
          { name: "PT Sans", data: bold, style: "normal", weight: 700 },
          { name: "PT Sans", data: regular, style: "normal", weight: 400 },
        ],
        headers: {
          // The card is fully determined by the query string, so a given URL
          // never changes — safe to cache aggressively (crawlers + CDN).
          "Cache-Control": "public, max-age=31536000, immutable",
        },
      },
    );
  } catch {
    return new Response("Failed to generate OG image", { status: 500 });
  }
}
