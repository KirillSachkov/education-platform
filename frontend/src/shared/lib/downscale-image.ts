type DownscaleOptions = {
  /** Longest-edge cap. Images already within this width pass through untouched. */
  maxWidth?: number;
  /** WebP quality, 0–1. */
  quality?: number;
};

/**
 * Downscale + re-encode an image to WebP in the browser BEFORE upload — shorter
 * upload spinner, smaller storage. Preserves aspect ratio.
 *
 * Returns the ORIGINAL file (never throws / never blocks the upload) when:
 *   - the file is not an image,
 *   - the image is already ≤ `maxWidth`,
 *   - any step fails (decode error, no canvas 2d context, toBlob null, etc.).
 *
 * Uses `createImageBitmap` + `<canvas>` + `toBlob("image/webp")`.
 */
export async function downscaleImage(
  file: File,
  { maxWidth = 1600, quality = 0.82 }: DownscaleOptions = {},
): Promise<File> {
  if (!file.type.startsWith("image/")) return file;

  try {
    const bitmap = await createImageBitmap(file);
    const { width, height } = bitmap;

    if (width <= maxWidth) {
      bitmap.close?.();
      return file;
    }

    const scale = maxWidth / width;
    const targetWidth = Math.round(width * scale);
    const targetHeight = Math.round(height * scale);

    const canvas = document.createElement("canvas");
    canvas.width = targetWidth;
    canvas.height = targetHeight;

    const ctx = canvas.getContext("2d");
    if (!ctx) {
      bitmap.close?.();
      return file;
    }

    ctx.drawImage(bitmap, 0, 0, targetWidth, targetHeight);
    bitmap.close?.();

    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, "image/webp", quality),
    );
    if (!blob) return file;

    const newName = file.name.replace(/\.[^.]+$/, "") + ".webp";
    return new File([blob], newName, { type: "image/webp", lastModified: Date.now() });
  } catch {
    return file;
  }
}
