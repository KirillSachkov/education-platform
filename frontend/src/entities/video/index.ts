// Types
export type {
  UploadProgress,
  UploadStatus,
  VideoInfo,
  VideoOwnerType,
  VideoValidationResult,
} from "./types";

// Re-export MediaStatus from shared for convenience
export type { MediaStatus } from "@/shared/types";

// API
export { videoApi } from "./api";
export type {
  AttachExistingVideoResponse,
  AttachExternalVideoRequest,
  GetVideoChaptersResponse,
  GetVideoResponse,
  GetVideosBatchRequest,
  GetVideosBatchResponse,
  InitiateVideoUploadRequest,
  InitiateVideoUploadResponse,
  ReplaceVideoChaptersRequest,
  UpdateVideoChapterItem,
  VideoChapter,
} from "./api";

// Model (hooks)
export {
  useDeleteVideo,
  type UseDeleteVideoOptions,
} from "./model/use-delete-video";

// Query options
export {
  videoQueryOptions,
  videoByEntityQueryOptions,
  videoChaptersQueryOptions,
} from "./model/query-options";

// Validators
export {
  formatFileSize,
  getVideoAcceptString,
  validateVideoFile,
  VIDEO_CONFIG,
} from "./lib/validators";

// UI Components
export { VideoCompletedState } from "./ui/video-completed-state";
export { VideoDropZone } from "./ui/video-drop-zone";
export { VideoErrorState } from "./ui/video-error-state";
export { VideoUploadingState } from "./ui/video-uploading-state";
