"use client";

import { useDeleteVideo } from "@/entities/video";
import { getFirstFile, getFirstFileFromClipboard } from "@/shared/lib/file-transfer";
import type { MediaOwnerType, VideoInfo } from "@/shared/types";
import { Button } from "@/shared/ui/kit/button";
import { Trash2 } from "lucide-react";
import { useState } from "react";
import { useAttachExternalVideo } from "../model/use-attach-external-video";
import { useVideoAssetPolling } from "../model/use-video-asset-polling";
import { useVideoPolling } from "../model/use-video-polling";
import { useVideoUpload } from "../model/use-video-upload";
import { VideoManageDialog } from "./video-manage-dialog";
import { VideoStatusCell } from "./video-status-cell";
import { VideoUploadDialog } from "./video-upload-dialog";

export type VideoUploadProps = {
  entityId?: string | null;
  entityType: MediaOwnerType;
  /** Optional draft id — used when uploading against a create-mode form. */
  draftId?: string | null;
  video: VideoInfo | null;
  /** Called when video state changes — `hasVideo` indicates whether a video exists after the change */
  onVideoChange?: (hasVideo: boolean) => void;
  /**
   * Called with the current asset id whenever the underlying video changes
   * (upload completed, attach-external success, or video deleted/cleared).
   * Works in both draft mode (no entityId) and edit mode — parent forms use
   * this to pass `videoId` in the create/update payload so backend can do
   * a sync bind/detach in the same transaction as the aggregate save.
   * `null` is passed when the video is removed.
   */
  onAssetIdChange?: (assetId: string | null) => void;
  /** Label for the entity in dialogs (e.g. "урока", "задачи") */
  entityLabel?: string;
};

export function VideoUpload({
  entityId,
  entityType,
  draftId,
  video: initialVideo,
  onVideoChange,
  onAssetIdChange,
}: VideoUploadProps) {
  const [isUploadDialogOpen, setIsUploadDialogOpen] = useState(false);
  const [isManageDialogOpen, setIsManageDialogOpen] = useState(false);
  const [pendingAssetId, setPendingAssetId] = useState<string | null>(null);
  const [isVideoDeleted, setIsVideoDeleted] = useState(false);

  const { deleteVideo, isDeleting } = useDeleteVideo({
    onSuccess: () => {
      setIsVideoDeleted(true);
      setPendingAssetId(null);
      setIsManageDialogOpen(false);
      onVideoChange?.(false);
      onAssetIdChange?.(null);
    },
  });

  const effectiveInitialVideo = isVideoDeleted ? null : initialVideo;

  // Entity polling is only for the aggregate's already-committed video. A fresh
  // upload is polled by exact asset id below so an old active slot cannot replace
  // the user's pending selection while autosave is still committing it.
  const { video, isPolling } = useVideoPolling({
    entityId: entityId ?? "",
    entityType,
    initialVideo: entityId ? effectiveInitialVideo : null,
    polling: false,
    onReady: () => {
      onVideoChange?.(true);
    },
  });

  const videoUpload = useVideoUpload({
    entityId,
    entityType,
    draftId,
    onComplete: (assetId) => {
      setIsVideoDeleted(false);
      setPendingAssetId(assetId);
      setIsUploadDialogOpen(false);
      onVideoChange?.(true);
      onAssetIdChange?.(assetId);
    },
  });

  const externalVideo = useAttachExternalVideo({
    entityId,
    entityType,
    draftId,
    onSuccess: (assetId) => {
      setIsVideoDeleted(false);
      setPendingAssetId(assetId);
      onVideoChange?.(true);
      onAssetIdChange?.(assetId);
    },
  });

  const handleStatusClick = () => {
    if (!displayVideo) {
      setIsUploadDialogOpen(true);
    } else if (displayVideo.status !== "ready") {
      setIsManageDialogOpen(true);
    }
  };

  const handleConfirmDelete = (deleteFromProvider: boolean) => {
    if (displayVideo?.id && entityId) {
      deleteVideo({
        videoId: displayVideo.id,
        deleteFromProvider,
        entityId,
        entityType,
      });
      return;
    }

    // Draft-mode: reset local upload state and notify parent to drop the
    // draft asset id from the pending bind list. The asset will be cleaned
    // up as an orphan during BindDraftAssets on submit (which RequestDeletes
    // any draft assets not in the requested list).
    if (!entityId && draftId) {
      videoUpload.reset();
      setIsVideoDeleted(true);
      setPendingAssetId(null);
      setIsManageDialogOpen(false);
      onAssetIdChange?.(null);
      onVideoChange?.(false);
    }
  };

  // The upload/attach response is the authoritative local selection in both
  // create and edit modes until the parent aggregate refreshes.
  const pendingInitialVideo: VideoInfo | null = pendingAssetId
    ? {
        id: pendingAssetId,
        videoId: pendingAssetId,
        status: externalVideo.data?.status === "ready" ? "ready" : "processing",
        thumbnailUrl: externalVideo.data?.thumbnailUrl ?? null,
        duration: externalVideo.data?.durationSeconds ?? null,
        isOwnedStorage: !externalVideo.data,
      }
    : null;

  const { video: pendingVideo, isPolling: isPendingPolling } = useVideoAssetPolling({
    assetId: pendingAssetId,
    initialVideo: pendingInitialVideo,
    polling: Boolean(pendingAssetId),
    onReady: () => {
      onVideoChange?.(true);
    },
  });

  const displayVideo = pendingAssetId ? pendingVideo : entityId ? video : null;
  const showPollingIndicator = pendingAssetId ? isPendingPolling : isPolling;

  const handleDirectUpload = (file: File) => {
    setIsVideoDeleted(false);
    setIsUploadDialogOpen(true);
    void videoUpload.upload(file);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = displayVideo ? "none" : "copy";
  };

  const handleDrop = (e: React.DragEvent) => {
    const file = getFirstFile(e.dataTransfer.files);
    if (!file) return;
    e.preventDefault();
    if (displayVideo) return;
    handleDirectUpload(file);
  };

  const handlePaste = (e: React.ClipboardEvent) => {
    const file = getFirstFileFromClipboard(e.clipboardData);
    if (!file || displayVideo) return;
    e.preventDefault();
    handleDirectUpload(file);
  };

  return (
    <div className="relative" onDragOver={handleDragOver} onDrop={handleDrop} onPaste={handlePaste}>
      <VideoStatusCell
        video={displayVideo}
        onClick={handleStatusClick}
        isPolling={showPollingIndicator}
        thumbnailUrl={displayVideo?.thumbnailUrl}
      />

      {displayVideo && (
        <div className="absolute top-2 right-2 z-10">
          <Button
            type="button"
            variant="secondary"
            size="icon"
            className="size-7 bg-white/90 dark:bg-gray-900/90 backdrop-blur-sm shadow-sm hover:bg-white dark:hover:bg-gray-900"
            onClick={() => setIsManageDialogOpen(true)}
          >
            <Trash2 size={13} className="text-muted-foreground" />
          </Button>
        </div>
      )}

      <VideoUploadDialog
        open={isUploadDialogOpen}
        onOpenChange={setIsUploadDialogOpen}
        uploadState={videoUpload.uploadState}
        onUpload={videoUpload.upload}
        onCancel={videoUpload.cancel}
        onReset={videoUpload.reset}
        onAttachExternal={externalVideo.attach}
        isAttaching={externalVideo.isLoading}
        attachError={externalVideo.error}
      />

      <VideoManageDialog
        open={isManageDialogOpen}
        onOpenChange={setIsManageDialogOpen}
        onConfirm={handleConfirmDelete}
        isDeleting={isDeleting}
        videoStatus={displayVideo?.status}
      />
    </div>
  );
}
