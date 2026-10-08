/**
 * Build a relative URL for a FileService-backed image so it can be passed to
 * `next/image` without per-environment `remotePatterns` config.
 *
 * The platform serves images via `/api/files/{id}/content` (nginx → FileService
 * → 302 to presigned S3). Relative paths are treated as same-origin by Next's
 * image optimizer — no `remotePatterns` entry needed, no env coupling.
 *
 * Avoid building absolute URLs from `API_ORIGIN` for images: they require
 * the host to be listed in `images.remotePatterns` (different per env), and
 * Next will throw at runtime if the entry is missing.
 */
export function fileImageSrc(fileId: string): string {
  return `/api/files/${fileId}/content`;
}

/** Convenience for nullable IDs — returns `undefined` for empty inputs. */
export function fileImageSrcOrNull(fileId: string | null | undefined): string | undefined {
  return fileId ? fileImageSrc(fileId) : undefined;
}
