import { WIDTHS } from "@/shared/lib/image-src";

const ASSET_URL_REGEX =
  /\/files\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/content/g;
const FILE_ASSET_HREF_REGEX =
  /\/files\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/content$/i;

export function getImageDimensions(
  file: File,
): Promise<{ width: number; height: number } | undefined> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      resolve({ width: img.naturalWidth, height: img.naturalHeight });
      URL.revokeObjectURL(url);
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(undefined);
    };
    img.src = url;
  });
}

export function extractAssetIds(markdown: string): string[] {
  const ids: string[] = [];
  let match: RegExpExecArray | null;

  ASSET_URL_REGEX.lastIndex = 0;
  while ((match = ASSET_URL_REGEX.exec(markdown)) !== null) {
    ids.push(match[1]);
  }

  return [...new Set(ids)];
}

export function isFileAssetHref(href: string | undefined | null): boolean {
  if (!href) return false;
  return FILE_ASSET_HREF_REGEX.test(href);
}

export function createUploadPlaceholder(fileName: string): string {
  return `![Uploading ${fileName}...]()`;
}

export function createFailurePlaceholder(fileName: string): string {
  return `![Upload failed: ${fileName}]()`;
}

export function createFileUploadPlaceholder(fileName: string): string {
  return `[Загружается ${fileName}...](#uploading)`;
}

export function createFileFailurePlaceholder(fileName: string): string {
  return `[Не удалось загрузить ${fileName}](#upload-failed)`;
}

export function buildImageMarkdown(
  fileName: string,
  assetId: string,
  dimensions?: { width: number; height: number },
): string {
  if (dimensions) {
    const safeAlt = fileName.replace(/"/g, "&quot;");
    const base = `/files/${assetId}/content`;
    // Width srcSet so the browser fetches the right-sized WebP variant the
    // backend serves via `?w=` (mirror of WIDTHS in image-src.ts). The body
    // renders at article width, so sizes caps at the 768px content column.
    const srcset = WIDTHS.map((w) => `${base}?w=${w} ${w}w`).join(", ");
    const sizes = "(min-width: 768px) 768px, 100vw";
    return `<img width="${dimensions.width}" height="${dimensions.height}" alt="${safeAlt}" src="${base}" srcset="${srcset}" sizes="${sizes}" loading="lazy" decoding="async" />`;
  }
  return `![${fileName}](/files/${assetId}/content)`;
}

export function buildFileMarkdown(fileName: string, assetId: string, sizeBytes?: number): string {
  const safeName = fileName.replace(/[\[\]()]/g, "");
  const sizeSuffix = sizeBytes ? ` (${formatFileSize(sizeBytes)})` : "";
  return `[${safeName}${sizeSuffix}](/files/${assetId}/content)`;
}

export function replacePlaceholder(text: string, placeholder: string, replacement: string): string {
  return text.replace(placeholder, replacement);
}

export function getFileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf(".");
  if (dot < 0 || dot === fileName.length - 1) return "";
  return fileName.slice(dot + 1).toLowerCase();
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} Б`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} КБ`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} МБ`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} ГБ`;
}

const EXTENSION_TO_CONTENT_TYPE: Record<string, string> = {
  excalidraw: "application/vnd.excalidraw+json",
  md: "text/markdown",
  markdown: "text/markdown",
  json: "application/json",
  csv: "text/csv",
  txt: "text/plain",
};

export function inferContentType(file: File): string {
  const ext = getFileExtension(file.name);
  // `||` not `??` — `file.type` is "" (empty string) for unknown extensions,
  // which is non-nullish but invalid as Content-Type. Empty string must fall
  // through to the octet-stream default.
  return EXTENSION_TO_CONTENT_TYPE[ext] || file.type || "application/octet-stream";
}

export function isImageFile(file: File): boolean {
  return file.type.startsWith("image/");
}
