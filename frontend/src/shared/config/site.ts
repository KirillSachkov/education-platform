const FALLBACK_APP_URL = "http://localhost";

export const APP_URL = (
  process.env.NEXT_PUBLIC_APP_URL ??
  process.env.NEXT_PUBLIC_AUTH_ORIGIN ??
  FALLBACK_APP_URL
).replace(/\/$/, "");

export function toAbsoluteUrl(path: string): string {
  return new URL(path, `${APP_URL}/`).toString();
}
