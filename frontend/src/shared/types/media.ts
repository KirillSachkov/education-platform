/**
 * Unified media status for video processing states.
 * Used across all entities that can own media (lessons, issues, etc.)
 */
export type MediaStatus =
  | "pending_upload"
  | "waiting_for_upload"
  | "uploading"
  | "uploaded"
  | "processing"
  | "ready"
  | "failed";

/**
 * Owner types - entities that can own media/videos
 */
export type MediaOwnerType = "lesson" | "issue" | "material";

export type VideoInfo = {
  id: string;
  videoId: string | null;
  status: MediaStatus;
  thumbnailUrl?: string | null;
  duration?: number | null;
  isOwnedStorage: boolean;
};
