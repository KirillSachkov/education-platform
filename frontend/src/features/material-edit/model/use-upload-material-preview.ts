import { invalidateEducationContent } from "@/entities/course";
import { fileApi, useFileUpload } from "@/entities/file";
import { getErrorMessage } from "@/shared/api";
import { EntityTypes } from "@/shared/config/entity-types";
import { downscaleImage } from "@/shared/lib/downscale-image";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { toast } from "sonner";

const MAX_SIZE = 10 * 1024 * 1024; // 10MB
const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];

export type MaterialPreviewUploadResult = {
  upload: (file: File) => void;
  isUploading: boolean;
  previewUrl: string | null;
  uploadedAssetId: string | null;
  isDeleted: boolean;
  remove: (imageId: string) => void;
  isRemoving: boolean;
};

type UseUploadMaterialPreviewOptions = {
  /** Material id — required for edit mode, omitted for create mode. */
  materialId?: string | null;
  /** Draft id — required for create mode, omitted for edit mode. */
  draftId?: string | null;
  /**
   * Optional handler — fires when uploadedAssetId меняется (после успешного
   * upload или delete). Используется для immediate-save в материал (не ждём
   * клика «Сохранить»). null = preview удалена.
   */
  onChange?: (assetId: string | null) => void;
};

/**
 * Cover image upload for a material. In edit mode (materialId provided),
 * FileService publishes `FileBound` and the ECS `MaterialPreviewBoundHandler`
 * wires the asset to the material. In create mode (draftId provided), the
 * asset stays as a draft until the parent form calls `bindDraftAssets` on
 * submit. We just invalidate the material detail query (when it exists) so
 * the UI picks up the new `imageId` / `imageUrl`.
 */
export function useUploadMaterialPreview(
  options: UseUploadMaterialPreviewOptions,
): MaterialPreviewUploadResult {
  const { materialId, draftId, onChange } = options;
  const queryClient = useQueryClient();
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [uploadedAssetId, setUploadedAssetId] = useState<string | null>(null);
  const [isDeleted, setIsDeleted] = useState(false);

  const { upload } = useFileUpload({
    usageType: "material_preview",
    targetEntity: materialId
      ? { type: EntityTypes.MATERIAL, id: materialId }
      : null,
    draftId: materialId ? null : (draftId ?? null),
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
    onSuccess: async ({ result: data, file }) => {
      // Show the local file instantly via blob URL instead of refetching from
      // storage; keep the asset id for the form/save payload.
      setPreviewUrl(URL.createObjectURL(file));
      setUploadedAssetId(data.assetId);
      setIsDeleted(false);
      toast.success("Обложка материала загружена");
      onChange?.(data.assetId);
      if (materialId) await invalidateEducationContent(queryClient);
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
      onChange?.(null);
    },
    onSuccess: async () => {
      toast.success("Обложка материала удалена");
      if (materialId) await invalidateEducationContent(queryClient);
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
