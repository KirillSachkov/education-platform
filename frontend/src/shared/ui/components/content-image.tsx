import { buildContentImageSrcSet, resolveImageUrl } from "@/shared/lib/image-src";
import { cn } from "@/shared/lib/css";
import type { ReactEventHandler } from "react";

type ContentImageProps = {
  /** FileService content URL (`/api/files/{id}/content`) or an external URL. */
  src: string | null | undefined;
  alt: string;
  /**
   * Responsive `sizes` hint — REQUIRED whenever a srcSet is produced, otherwise
   * the browser assumes `100vw` and downloads the largest bucket. Pass the
   * rendered CSS width per breakpoint, e.g. `"(min-width: 640px) 8rem, 6rem"`.
   */
  sizes?: string;
  className?: string;
  loading?: "lazy" | "eager";
  decoding?: "sync" | "async" | "auto";
  fetchPriority?: "high" | "low" | "auto";
  width?: number;
  height?: number;
  /**
   * Drop-in for `next/image`'s `fill` — stretches the image to fill a
   * `position: relative` (or aspect-ratio) parent via `absolute inset-0
   * size-full`. The migration target for `<Image fill className="X" />`
   * call-sites; pass the cover/object-fit class via `className`.
   */
  fill?: boolean;
  /** Forwarded to the underlying `<img>` (e.g. swap to a fallback on a broken/404 asset). */
  onError?: ReactEventHandler<HTMLImageElement>;
  /** Forwarded to the underlying `<img>` (e.g. detect placeholder/1×1 assets via naturalWidth). */
  onLoad?: ReactEventHandler<HTMLImageElement>;
};

/**
 * Thin `<img>` wrapper for FileService-backed content images. Computes a width
 * `srcSet` (`?w=320|640|960|1280`) so the browser fetches the right-sized WebP
 * variant from the backend; external URLs (Kinescope posters, GitHub avatars)
 * get a plain `src` with no srcSet — the backend can't resize those.
 *
 * Intentionally NOT `next/image`: the backend already does the resize + WebP
 * negotiation behind `?w=`, so routing through the Next optimizer with a custom
 * loader would double-resize and re-encode. A bare `<img>` with srcSet is the
 * correct primitive here.
 */
export function ContentImage({
  src,
  alt,
  sizes,
  className,
  loading = "lazy",
  decoding = "async",
  fetchPriority,
  width,
  height,
  fill = false,
  onError,
  onLoad,
}: ContentImageProps) {
  const resolved = resolveImageUrl(src) ?? undefined;
  const srcSet = buildContentImageSrcSet(src);

  if (process.env.NODE_ENV !== "production" && srcSet && !sizes) {
    console.warn(
      `ContentImage: a srcSet was generated for "${resolved}" but no \`sizes\` ` +
        `was passed — the browser assumes 100vw and always downloads the 1280w ` +
        `variant, negating the responsive image. Pass a \`sizes\` matching the ` +
        `rendered width (e.g. sizes="(min-width: 640px) 8rem, 6rem").`,
    );
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element -- backend does resize/WebP via ?w=; next/image would double-resize
    <img
      src={resolved}
      srcSet={srcSet}
      sizes={srcSet ? sizes : undefined}
      alt={alt}
      className={fill ? cn("absolute inset-0 size-full", className) : className}
      loading={loading}
      decoding={decoding}
      fetchPriority={fetchPriority}
      width={width}
      height={height}
      onError={onError}
      onLoad={onLoad}
    />
  );
}
