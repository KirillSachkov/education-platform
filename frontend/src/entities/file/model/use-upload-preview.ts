import { fileApi, useFileUpload } from "@/entities/file";
import { getErrorMessage } from "@/shared/api";
import type { EntityType } from "@/shared/config/entity-types";
import { downscaleImage } from "@/shared/lib/downscale-image";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { toast } from "sonner";

const MAX_SIZE = 10 * 1024 * 1024; // 10MB
const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];

type PreviewUploadConfig = {
  usageType: "course_preview" | "collection_cover";
  entityType: EntityType;
  entityId: string;
  invalidateKey: readonly unknown[];
  labels?: {
    uploaded?: string;
    deleted?: string;
  };
};

export function useUploadPreview({
  usageType,
  entityType,
  entityId,
  invalidateKey,
  labels,
}: PreviewUploadConfig) {
  const queryClient = useQueryClient();
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [uploadedAssetId, setUploadedAssetId] = useState<string | null>(null);
  const [isDeleted, setIsDeleted] = useState(false);

  const { upload } = useFileUpload({
    usageType,
    targetEntity: { type: entityType, id: entityId },
  });

  // Revoke any blob: preview URL on replacement / unmount to avoid leaks.
  // React Compiler: revoke lives in effect cleanup, never during render.
  useEffect(() => {
    if (!previewUrl?.startsWith("blob:")) return;
    return () => URL.revokeObjectURL(previewUrl);
  }, [previewUrl]);

  const uploadMutation = useMutation({
    mutationFn: async (file: File) => {
      if (!ALLOWED_TYPES.includes(file.type)) {
        throw new Error("Допустимые форматы: JPEG, PNG, WebP");
      }
      if (file.size > MAX_SIZE) {
        throw new Error("Максимальный размер файла: 10 МБ");
      }

      const downscaled = await downscaleImage(file);
      const result = await upload(downscaled);
      return { result, file: downscaled };
    },
    onSuccess: ({ result: data, file }) => {
      // Show the local file instantly via blob URL instead of refetching from
      // storage; keep the asset id for the form/save payload.
      setPreviewUrl(URL.createObjectURL(file));
      setUploadedAssetId(data.assetId);
      setIsDeleted(false);
      queryClient.invalidateQueries({ queryKey: invalidateKey });
      toast.success(labels?.uploaded ?? "Обложка загружена");
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка загрузки обложки"));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (imageId: string) => fileApi.deleteFile(imageId),
    onMutate: () => {
      setIsDeleted(true);
      setPreviewUrl(null);
      setUploadedAssetId(null);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: invalidateKey });
      toast.success(labels?.deleted ?? "Обложка удалена");
    },
    onError: (error) => {
      setIsDeleted(false);
      toast.error(getErrorMessage(error, "Ошибка удаления обложки"));
    },
  });

  return {
    upload: uploadMutation.mutate,
    isUploading: uploadMutation.isPending,
    previewUrl,
    uploadedAssetId,
    isDeleted,
    remove: (imageId: string) => deleteMutation.mutate(imageId),
    isRemoving: deleteMutation.isPending,
  };
}
