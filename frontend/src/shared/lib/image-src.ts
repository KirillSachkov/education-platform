import { API_ORIGIN } from "@/shared/api";

/**
 * Responsive-image width buckets. MUST mirror the backend `?w=` variant buckets
 * (EducationContentService / FileService image resize). The server returns the
 * nearest-larger WebP variant, falling back to the original — so requesting a
 * bucket that the backend hasn't generated yet is always safe.
 */
export const WIDTHS = [320, 640, 960, 1280] as const;

/**
 * Resolve a FileService-backed image URL to an absolute browser URL.
 *
 * Relative paths (`/api/files/{id}/content`) are prefixed with `API_ORIGIN`;
 * already-absolute URLs and external URLs pass through unchanged. Returns
 * `null` for empty input.
 *
 * Single source of truth for the inline `url && url.startsWith("/") ? ...`
 * snippet previously copy-pasted across upload components.
 */
export function resolveImageUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  return url.startsWith("/") ? `${API_ORIGIN}${url}` : url;
}

/** Path shape of OUR content endpoint, anchored to the start of the path. */
const CONTENT_PATH = /^\/(?:api\/)?files\/[^/]+\/content(?:\?|#|$)/;

/**
 * True when `url` points at our own FileService content endpoint
 * (`/files/{id}/content` or `/api/files/{id}/content`), either relative or
 * absolute on OUR origin (`${API_ORIGIN}/...`). External media — Kinescope
 * posters, Unsplash, GitHub avatars, an attacker host like
 * `https://evil.com/files/x/content` — return false.
 *
 * This is a guard, not a substring match: only same-origin/relative content
 * paths qualify, so a generated `srcSet` can never point at a foreign host.
 */
export function isContentImageUrl(url: string | null | undefined): boolean {
  if (!url) return false;
  // Relative path — must start with our content path shape.
  if (url.startsWith("/")) return CONTENT_PATH.test(url);
  // Absolute URL — must be on OUR origin AND match the content path shape.
  try {
    const parsed = new URL(url);
    if (parsed.origin !== API_ORIGIN) return false;
    return CONTENT_PATH.test(`${parsed.pathname}${parsed.search}${parsed.hash}`);
  } catch {
    return false;
  }
}

/**
 * Build a `srcSet` string for one of OUR content images so the browser can
 * request the right-sized WebP variant from the backend via `?w=`.
 *
 * Returns `undefined` (no srcSet) for external URLs or null input — the caller
 * then renders a plain `src` with no responsive negotiation, which is correct
 * for Kinescope posters / third-party avatars the backend can't resize.
 *
 * Preserves any existing query params on the base URL (appends `&w=` instead of
 * `?w=` when a query is already present).
 */
export function buildContentImageSrcSet(url: string | null | undefined): string | undefined {
  if (!isContentImageUrl(url)) return undefined;
  const base = resolveImageUrl(url)!;
  const separator = base.includes("?") ? "&" : "?";
  return WIDTHS.map((w) => `${base}${separator}w=${w} ${w}w`).join(", ");
}
