const FOURTEEN_DAYS_MS = 14 * 24 * 60 * 60 * 1000;

export function isRecentlyPublished(publishedAt: string | null): boolean {
  if (!publishedAt) return false;
  return new Date(publishedAt).getTime() > Date.now() - FOURTEEN_DAYS_MS;
}
