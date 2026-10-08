import { useEffect, useRef, useState } from "react";

import {
  fileApi,
  type CompleteFileUploadResponse,
  type InitiateFileUploadRequest,
} from "../api";

type UploadParams = {
  usageType: string;
  targetEntity?: { type: string; id: string } | null;
  draftId?: string | null;
};

type UseFileUploadReturn = {
  upload: (file: File) => Promise<CompleteFileUploadResponse>;
  isUploading: boolean;
  error: string | null;
  reset: () => void;
};

export function useFileUpload(params: UploadParams): UseFileUploadReturn {
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const paramsRef = useRef(params);
  useEffect(() => {
    paramsRef.current = params;
  });

  const upload = async (file: File): Promise<CompleteFileUploadResponse> => {
    setIsUploading(true);
    setError(null);

    try {
      const request: InitiateFileUploadRequest = {
        fileName: file.name,
        contentType: file.type,
        size: file.size,
        usageType: paramsRef.current.usageType,
        draftId: paramsRef.current.draftId ?? null,
        targetEntity: paramsRef.current.targetEntity ?? null,
      };

      const initResponse = await fileApi.initiateUpload(request);

      let uploadResponse: Response;
      try {
        uploadResponse = await fetch(initResponse.uploadUrl, {
          method: "PUT",
          body: file,
          headers: {
            ...initResponse.requiredHeaders,
            "Content-Type": file.type,
          },
        });
      } catch {
        throw new Error("Не удалось загрузить файл в хранилище");
      }

      if (!uploadResponse.ok) {
        throw new Error("Не удалось загрузить файл в хранилище");
      }

      const completeResponse = await fileApi.completeUpload(
        initResponse.assetId,
      );

      return completeResponse;
    } catch (err) {
      setError("Ошибка загрузки файла");
      throw err;
    } finally {
      setIsUploading(false);
    }
  };

  const reset = () => {
    setIsUploading(false);
    setError(null);
  };

  return { upload, isUploading, error, reset };
}
