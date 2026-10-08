import { apiClient } from "@/shared/api";
import type { MediaStatus } from "@/shared/types";

export type InitiateVideoUploadRequest = {
  fileName: string;
  contentType: string;
  size: number;
  usageType: string;
  targetEntity?: { type: string; id: string } | null;
  draftId?: string | null;
};

export type InitiateVideoUploadResponse = {
  assetId: string;
  status: string;
  uploadUrl: string;
  providerVideoId: string;
};

export type GetVideoResponse = {
  id: string;
  fileName: string;
  status: MediaStatus;
  videoId: string | null;
  thumbnailUrl: string | null;
  duration: number | null;
  isOwnedStorage: boolean;
};

export type GetVideosBatchRequest = {
  ids: string[];
};

export type GetVideosBatchResponse = {
  videos: GetVideoResponse[];
};

export type VideoChapter = {
  id: string;
  title: string;
  startSeconds: number;
  sortOrder: number;
};

export type GetVideoChaptersResponse = {
  videoId: string;
  chapters: VideoChapter[];
};

export type UpdateVideoChapterItem = {
  id: string | null;
  title: string;
  startSeconds: number;
  sortOrder: number;
};

export type ReplaceVideoChaptersRequest = {
  chapters: UpdateVideoChapterItem[];
};

export type AttachExternalVideoRequest = {
  externalVideoId: string;
  usageType: string;
  targetEntity?: { type: string; id: string } | null;
  draftId?: string | null;
};

export type AttachExistingVideoResponse = {
  assetId: string;
  status: string;
  thumbnailUrl: string | null;
  durationSeconds: number | null;
};

export const videoApi = {
  initiateUpload: async (
    request: InitiateVideoUploadRequest,
  ): Promise<InitiateVideoUploadResponse> => {
    const response = await apiClient.post<{
      result: InitiateVideoUploadResponse;
    }>("/videos/uploads/", request);
    return response.data.result;
  },

  deleteVideo: async (
    videoId: string,
    deleteFromProvider: boolean,
  ): Promise<void> => {
    await apiClient.delete(`/videos/${videoId}`, {
      params: { deleteFromProvider },
    });
  },

  attachExternalVideo: async (
    request: AttachExternalVideoRequest,
  ): Promise<AttachExistingVideoResponse> => {
    const response = await apiClient.post<{
      result: AttachExistingVideoResponse;
    }>("/videos/attach-existing/", request);
    return response.data.result;
  },

  getVideoByEntity: async (
    entityId: string,
    entityType: string,
  ): Promise<GetVideoResponse | null> => {
    const response = await apiClient.get<{
      result: GetVideoResponse | null;
    }>("/videos/by-entity", {
      params: { entityId, entityType },
    });
    return response.data.result;
  },

  getVideo: async (videoId: string): Promise<GetVideoResponse | null> => {
    const response = await apiClient.get<{
      result: GetVideoResponse | null;
    }>(`/videos/${videoId}`);
    return response.data.result;
  },

  getChapters: async (
    videoId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<GetVideoChaptersResponse> => {
    const response = await apiClient.get<{ result: GetVideoChaptersResponse }>(
      `/videos/${videoId}/chapters/`,
      { signal },
    );
    return response.data.result;
  },

  replaceChapters: async ({
    videoId,
    request,
  }: {
    videoId: string;
    request: ReplaceVideoChaptersRequest;
  }): Promise<GetVideoChaptersResponse> => {
    const response = await apiClient.put<{ result: GetVideoChaptersResponse }>(
      `/videos/${videoId}/chapters/`,
      request,
    );
    return response.data.result;
  },
};
