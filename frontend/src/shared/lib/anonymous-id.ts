/**
 * Stable anonymous visitor identifier persisted in cookie `plu_anon_id` (UUID v4,
 * max-age ~2 years, SameSite=Lax). Used by `useTrackMaterialView` for anonymous
 * visitors so ProgressService can dedupe views by (cookie, materialId) instead of
 * counting every page open. Issue #234.
 *
 * Browser-only — returns `null` during SSR.
 */
const COOKIE_NAME = "plu_anon_id";
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 365 * 2; // ~2 years
const UUID_V4_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function getOrCreateAnonymousId(): string | null {
  if (typeof document === "undefined") {
    return null;
  }

  const existing = readCookie(COOKIE_NAME);
  if (existing && UUID_V4_RE.test(existing)) {
    return existing;
  }

  const fresh = crypto.randomUUID();
  writeCookie(COOKIE_NAME, fresh);
  return fresh;
}

function readCookie(name: string): string | null {
  // document.cookie returns a single string `"a=1; b=2"` — split & match by name.
  const pairs = document.cookie ? document.cookie.split(";") : [];
  for (const raw of pairs) {
    const eq = raw.indexOf("=");
    if (eq < 0) continue;
    const key = raw.slice(0, eq).trim();
    if (key === name) {
      return decodeURIComponent(raw.slice(eq + 1).trim());
    }
  }
  return null;
}

function writeCookie(name: string, value: string) {
  const encoded = encodeURIComponent(value);
  const attrs = [
    `${name}=${encoded}`,
    "Path=/",
    `Max-Age=${COOKIE_MAX_AGE_SECONDS}`,
    "SameSite=Lax",
  ];
  if (window.location.protocol === "https:") {
    attrs.push("Secure");
  }
  document.cookie = attrs.join("; ");
}
