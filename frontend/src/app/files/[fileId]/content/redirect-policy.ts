export const FILE_API_URL =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

const ALLOWED_REDIRECT_PATTERNS = [
  /^localhost$/, // dev
  /(^|\.)storage\.yandexcloud\.net$/, // Yandex Cloud S3 (path-style and virtual-hosted)
  /\.kinescope\.io$/, // Kinescope CDN
  /\.kinescopecdn\.net$/, // Kinescope CDN alt
];

export function isAllowedRedirect(url: string): boolean {
  try {
    const parsed = new URL(url);
    const apiOrigin = new URL(FILE_API_URL).origin;
    if (parsed.origin === apiOrigin) return true;
    return ALLOWED_REDIRECT_PATTERNS.some((pattern) => pattern.test(parsed.hostname));
  } catch {
    return false;
  }
}
