import type { VideoValidationResult } from "../types";

// ============================================================================
// Size Constants
// ============================================================================

const KB = 1024;
const MB = KB * 1024;
const GB = MB * 1024;

// ============================================================================
// Video Config
// ============================================================================

export const VIDEO_CONFIG = {
  maxSize: 5 * GB,
  allowedExtensions: [".mp4", ".webm", ".mov", ".avi", ".mkv"],
  allowedMimeTypes: [
    "video/mp4",
    "video/webm",
    "video/quicktime",
    "video/x-msvideo",
    "video/x-matroska",
  ],
  label: "Видео",
  description: "MP4, WebM, MOV до 5 ГБ",
} as const;

// ============================================================================
// Validation Functions
// ============================================================================

/**
 * Format bytes to human readable string
 */
export function formatFileSize(bytes: number): string {
  if (bytes < KB) return `${bytes} Б`;
  if (bytes < MB) return `${(bytes / KB).toFixed(1)} КБ`;
  if (bytes < GB) return `${(bytes / MB).toFixed(1)} МБ`;
  return `${(bytes / GB).toFixed(2)} ГБ`;
}

/**
 * Get file extension from filename
 */
function getFileExtension(fileName: string): string {
  const lastDot = fileName.lastIndexOf(".");
  if (lastDot === -1) return "";
  return fileName.slice(lastDot).toLowerCase();
}

/**
 * Validate video file
 */
export function validateVideoFile(file: File): VideoValidationResult {
  // Check file size
  if (file.size > VIDEO_CONFIG.maxSize) {
    return {
      valid: false,
      error: `Файл слишком большой. Максимальный размер: ${formatFileSize(VIDEO_CONFIG.maxSize)}`,
    };
  }

  // Check file size is not zero
  if (file.size === 0) {
    return {
      valid: false,
      error: "Файл пустой",
    };
  }

  // Check MIME type
  if (
    !VIDEO_CONFIG.allowedMimeTypes.includes(
      file.type as (typeof VIDEO_CONFIG.allowedMimeTypes)[number],
    )
  ) {
    return {
      valid: false,
      error: `Неподдерживаемый тип файла. Разрешены: ${VIDEO_CONFIG.allowedExtensions.join(", ")}`,
    };
  }

  // Check extension
  const extension = getFileExtension(file.name);
  if (
    !VIDEO_CONFIG.allowedExtensions.includes(
      extension as (typeof VIDEO_CONFIG.allowedExtensions)[number],
    )
  ) {
    return {
      valid: false,
      error: `Неподдерживаемое расширение файла. Разрешены: ${VIDEO_CONFIG.allowedExtensions.join(", ")}`,
    };
  }

  return { valid: true };
}

/**
 * Get accept string for file input
 */
export function getVideoAcceptString(): string {
  return VIDEO_CONFIG.allowedMimeTypes.join(",");
}
