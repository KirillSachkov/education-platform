import {
  validateVideoFile,
  videoApi,
  type UploadProgress,
} from "@/entities/video";
import { useRef, useState } from "react";
import * as tus from "tus-js-client";

const CHUNK_SIZE = 10_000_000; // 10MB
const RETRY_DELAYS = [0, 3000, 5000, 10000, 20000];

export type UseVideoUploadOptions = {
  entityId?: string | null;
  entityType: string;
  /** Optional draft id — used when the parent entity does not exist yet. */
  draftId?: string | null;
  onComplete?: (videoId: string) => void;
};

export function useVideoUpload(options: UseVideoUploadOptions) {
  const { entityId, entityType, draftId, onComplete } = options;

  const [uploadState, setUploadState] = useState<UploadProgress>({
    status: "idle",
    progress: 0,
    uploadedBytes: 0,
    totalBytes: 0,
  });

  const tusUploadRef = useRef<tus.Upload | null>(null);
  const currentUploadRef = useRef<{ videoId: string } | null>(null);

  const upload = async (file: File): Promise<string | undefined> => {
    const validation = validateVideoFile(file);
    if (!validation.valid) {
      setUploadState({
        status: "failed",
        progress: 0,
        uploadedBytes: 0,
        totalBytes: file.size,
        fileName: file.name,
        fileSize: file.size,
        error: validation.error,
      });
      return undefined;
    }

    try {
      setUploadState({
        status: "uploading",
        progress: 0,
        uploadedBytes: 0,
        totalBytes: file.size,
        fileName: file.name,
        fileSize: file.size,
      });

      const usageType =
        entityType === "material"
          ? "material_video"
          : entityType === "lesson"
            ? "lesson_video"
            : "course_video";
      const response = await videoApi.initiateUpload({
        fileName: file.name,
        contentType: file.type || "video/mp4",
        size: file.size,
        usageType,
        targetEntity: entityId ? { type: entityType, id: entityId } : null,
        draftId: draftId ?? null,
      });

      const videoId = response.assetId;
      const { uploadUrl } = response;
      currentUploadRef.current = { videoId };

      await new Promise<void>((resolve, reject) => {
        const tusUpload = new tus.Upload(file, {
          uploadUrl,
          chunkSize: CHUNK_SIZE,
          retryDelays: RETRY_DELAYS,
          metadata: {
            filename: file.name,
            filetype: file.type || "video/mp4",
          },
          onError: (error) => {
            reject(error);
          },
          onProgress: (bytesUploaded, bytesTotal) => {
            const progress = Math.round((bytesUploaded / bytesTotal) * 100);
            setUploadState((prev) => ({
              ...prev,
              progress,
              uploadedBytes: bytesUploaded,
              totalBytes: bytesTotal,
            }));
          },
          onSuccess: () => {
            resolve();
          },
        });

        tusUploadRef.current = tusUpload;
        tusUpload.start();
      });

      setUploadState({
        status: "completed",
        progress: 100,
        uploadedBytes: file.size,
        totalBytes: file.size,
        fileName: file.name,
        fileSize: file.size,
        videoId,
      });

      onComplete?.(videoId);
      return videoId;
    } catch {
      const errorMessage = "Ошибка загрузки видео";

      setUploadState((prev) => ({
        ...prev,
        status: "failed",
        error: errorMessage,
      }));

      return undefined;
    }
  };

  const cancel = async () => {
    if (tusUploadRef.current) {
      tusUploadRef.current.abort();
      tusUploadRef.current = null;
    }

    const current = currentUploadRef.current;
    if (current) {
      try {
        await videoApi.deleteVideo(current.videoId, true);
      } catch {}
      currentUploadRef.current = null;
    }

    setUploadState({
      status: "idle",
      progress: 0,
      uploadedBytes: 0,
      totalBytes: 0,
    });
  };

  const reset = () => {
    tusUploadRef.current = null;
    currentUploadRef.current = null;
    setUploadState({
      status: "idle",
      progress: 0,
      uploadedBytes: 0,
      totalBytes: 0,
    });
  };

  return {
    upload,
    cancel,
    reset,
    uploadState,
    isIdle: uploadState.status === "idle",
    isUploading: uploadState.status === "uploading",
    isCompleted: uploadState.status === "completed",
    isFailed: uploadState.status === "failed",
  };
}
