import { useMarkdownAssetUpload } from "./use-markdown-asset-upload";
import { downscaleImage } from "@/shared/lib/downscale-image";
import { buildImageMarkdown, getImageDimensions } from "@/shared/lib/markdown-assets";

const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];
const MAX_SIZE = 10 * 1024 * 1024; // 10MB

type Dimensions = { width: number; height: number } | undefined;

type UseMarkdownImageUploadParams = {
  draftId?: string;
  targetEntity?: { type: string; id: string };
};

export function useMarkdownImageUpload(params: UseMarkdownImageUploadParams) {
  const { handleAttach, getUploadedAssetIds } = useMarkdownAssetUpload<Dimensions>({
    usageType: "markdown_image",
    draftId: params.draftId,
    targetEntity: params.targetEntity,
    validate: async (file) => {
      if (!ALLOWED_TYPES.includes(file.type)) {
        return { ok: false, message: "Допустимые форматы: JPEG, PNG, WebP" };
      }
      if (file.size > MAX_SIZE) {
        return { ok: false, message: "Максимальный размер изображения: 10 МБ" };
      }
      const dimensions = await getImageDimensions(file);
      return { ok: true, meta: dimensions };
    },
    // Downscale + WebP-encode in the browser before upload (smaller storage,
    // shorter spinner). Falls back to the original on any error.
    prepareFile: (file) => downscaleImage(file),
    buildMarkdown: (file, assetId, dimensions) =>
      buildImageMarkdown(file.name, assetId, dimensions),
    uploadErrorMessage: "Не удалось загрузить изображение",
  });

  return {
    handleImagePaste: handleAttach,
    getUploadedAssetIds,
  };
}
