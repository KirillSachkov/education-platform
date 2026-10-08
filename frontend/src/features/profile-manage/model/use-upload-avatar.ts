"use client";

import { fileApi, useFileUpload } from "@/entities/file";
import { profileQueryOptions } from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { downscaleImage } from "@/shared/lib/downscale-image";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { toast } from "sonner";

const MAX_SIZE = 5 * 1024 * 1024; // 5MB
const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];

export function useUploadAvatar(userId: string) {
  const queryClient = useQueryClient();
  const [avatarUrl, setAvatarUrl] = useState<string | null>(null);
  const [uploadedAssetId, setUploadedAssetId] = useState<string | null>(null);
  const [isDeleted, setIsDeleted] = useState(false);

  const { upload } = useFileUpload({
    usageType: "avatar",
    targetEntity: { type: "user", id: userId },
  });

  // Revoke any blob: preview URL on replacement / unmount to avoid leaks.
  // React Compiler: revoke lives in effect cleanup, never during render.
  useEffect(() => {
    if (!avatarUrl?.startsWith("blob:")) return;
    return () => URL.revokeObjectURL(avatarUrl);
  }, [avatarUrl]);

  const uploadMutation = useMutation({
    mutationFn: async (file: File) => {
      if (!ALLOWED_TYPES.includes(file.type)) {
        throw new Error("Допустимые форматы: JPEG, PNG, WebP");
      }
      if (file.size > MAX_SIZE) {
        throw new Error("Максимальный размер файла: 5 МБ");
      }

      const downscaled = await downscaleImage(file, { maxWidth: 512 });
      const result = await upload(downscaled);
      return { result, file: downscaled };
    },
    onSuccess: ({ result: data, file }) => {
      // Show the local file instantly via blob URL instead of refetching from
      // storage; keep the asset id for the form/save payload.
      setAvatarUrl(URL.createObjectURL(file));
      setUploadedAssetId(data.assetId);
      setIsDeleted(false);
      queryClient.invalidateQueries({
        queryKey: profileQueryOptions.getMyProfileKey(),
      });
      toast.success("Аватар загружен");
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка загрузки аватара"));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (imageId: string) => fileApi.deleteFile(imageId),
    onMutate: () => {
      setIsDeleted(true);
      setAvatarUrl(null);
      setUploadedAssetId(null);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: profileQueryOptions.getMyProfileKey(),
      });
      toast.success("Аватар удалён");
    },
    onError: (error) => {
      setIsDeleted(false);
      toast.error(getErrorMessage(error, "Ошибка удаления аватара"));
    },
  });

  return {
    upload: uploadMutation.mutate,
    isUploading: uploadMutation.isPending,
    avatarUrl,
    uploadedAssetId,
    isDeleted,
    remove: (imageId: string) => deleteMutation.mutate(imageId),
    isRemoving: deleteMutation.isPending,
  };
}
