/**
 * Safe redirect target — accept only same-origin relative paths. Blocks
 * open-redirect attacks via `?callbackUrl=` / `?next=`:
 * - absolute URLs with a scheme (`https://evil.com`, `javascript:alert(1)`)
 *   — don't start with "/";
 * - protocol-relative (`//evil.com`) — the browser leaves for a foreign host;
 * - any backslash (`/\evil.com`, `/a\b`) — browsers normalize `\` to `/`,
 *   turning it into the protocol-relative case.
 * Anything rejected falls back to `fallback` ("/" by default).
 */
export function sanitizeCallbackUrl(raw: string | undefined | null, fallback = "/"): string {
  if (!raw) return fallback;
  if (!raw.startsWith("/") || raw.startsWith("//") || raw.includes("\\")) {
    return fallback;
  }
  return raw;
}
