import { apiClient } from "@/shared/api";

export type InitiateFileUploadRequest = {
  fileName: string;
  contentType: string;
  size: number;
  usageType: string;
  draftId?: string | null;
  targetEntity?: { type: string; id: string } | null;
};

export type InitiateFileUploadResponse = {
  assetId: string;
  status: string;
  uploadUrl: string;
  requiredHeaders: Record<string, string>;
  contentUrl: string;
};

export type CompleteFileUploadResponse = {
  assetId: string;
  status: string;
  contentUrl: string;
};

export const fileApi = {
  initiateUpload: async (
    request: InitiateFileUploadRequest,
  ): Promise<InitiateFileUploadResponse> => {
    const response = await apiClient.post<{
      result: InitiateFileUploadResponse;
    }>("/files/uploads/", request);
    return response.data.result;
  },

  completeUpload: async (
    assetId: string,
    checksum?: string | null,
  ): Promise<CompleteFileUploadResponse> => {
    const response = await apiClient.post<{
      result: CompleteFileUploadResponse;
    }>(`/files/${assetId}/complete`, { checksum: checksum ?? null });
    return response.data.result;
  },

  deleteFile: async (fileId: string): Promise<void> => {
    await apiClient.delete(`/files/${fileId}`);
  },

  bindDraftAssets: async (request: {
    draftId: string;
    targetEntity: { type: string; id: string };
    assetIds: string[];
  }): Promise<void> => {
    await apiClient.post("/draft-assets/bind", request);
  },

  syncEntityAssets: async (request: {
    targetEntity: { type: string; id: string };
    usageTypes: string[];
    activeAssetIds: string[];
  }): Promise<void> => {
    await apiClient.post("/assets/sync", request);
  },
};
