"use client";

import { ContentImage } from "@/shared/ui/components/content-image";
import { useState, type ReactNode } from "react";

interface CollectionCoverImageProps {
  src: string | null | undefined;
  alt: string;
  /** Rendered when src is null, the image errors, or its natural dimensions are below `minDimension`. */
  fallback: ReactNode;
  /** Treat the image as invalid if its natural width or height is smaller than this. Defaults to 32px. */
  minDimension?: number;
  /** Stretch to fill a relative/aspect-ratio parent (maps to ContentImage's `fill`). */
  fill?: boolean;
  /** Responsive `sizes` hint (required when a srcSet is produced for our content). */
  sizes?: string;
  className?: string;
  loading?: "lazy" | "eager";
  fetchPriority?: "high" | "low" | "auto";
  /**
   * Above-the-fold hint — kept as a named prop for call-site parity with the
   * old next/image API; maps to eager loading + high fetch priority.
   */
  priority?: boolean;
}

/**
 * Cover image with a guaranteed visual fallback. Detects placeholder/broken assets
 * (e.g. legacy 1×1 PNGs) via `onLoad` and swaps to the fallback rather than rendering
 * a stretched single pixel as a fake "cover".
 *
 * Backed by `ContentImage` so FileService covers fetch the right-sized `?w=` WebP
 * variant; external/legacy URLs degrade to a plain `<img>` (no srcSet) automatically.
 */
export function CollectionCoverImage({
  src,
  alt,
  fallback,
  minDimension = 32,
  fill = false,
  sizes,
  className,
  loading,
  fetchPriority,
  priority = false,
}: CollectionCoverImageProps) {
  const [broken, setBroken] = useState(false);

  if (!src || broken) {
    return <>{fallback}</>;
  }

  return (
    <ContentImage
      src={src}
      alt={alt}
      fill={fill}
      sizes={sizes}
      className={className}
      loading={loading ?? (priority ? "eager" : "lazy")}
      fetchPriority={fetchPriority ?? (priority ? "high" : undefined)}
      onError={() => setBroken(true)}
      onLoad={(e) => {
        const img = e.currentTarget;
        if (img.naturalWidth < minDimension || img.naturalHeight < minDimension) {
          setBroken(true);
        }
      }}
    />
  );
}
