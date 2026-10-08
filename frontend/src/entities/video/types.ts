import type { MediaStatus } from "@/shared/types";

/**
 * Video info returned from backend
 */
export type VideoInfo = {
  id: string;
  fileName: string;
  status: MediaStatus;
  videoId: string | null;
  thumbnailUrl: string | null;
};

/**
 * Upload progress state
 */
export type UploadStatus = "idle" | "uploading" | "completed" | "failed";

export type UploadProgress = {
  status: UploadStatus;
  progress: number;
  uploadedBytes: number;
  totalBytes: number;
  fileName?: string;
  fileSize?: number;
  error?: string;
  videoId?: string;
};

/**
 * Video validation result
 */
export type VideoValidationResult =
  | { valid: true }
  | { valid: false; error: string };

// Re-export MediaOwnerType as VideoOwnerType for backward compatibility
export type { MediaOwnerType as VideoOwnerType } from "@/shared/types";
