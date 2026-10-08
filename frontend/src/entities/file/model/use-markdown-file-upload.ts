import { useMarkdownAssetUpload } from "./use-markdown-asset-upload";
import { buildFileMarkdown, inferContentType } from "@/shared/lib/markdown-assets";

// Keep in sync with AssetUsagePolicyCatalog.cs `AssetUsageType.MARKDOWN_FILE`
// (backend). On mismatch, frontend rejects with a confusing toast OR backend
// returns "policy violation" — both are inconsistent UX. Update both files.
const ALLOWED_FILE_TYPES = new Set([
  "application/pdf",
  "text/plain",
  "text/csv",
  "text/markdown",
  "application/json",
  "application/vnd.excalidraw+json",
  "application/zip",
  "application/x-tar",
  "application/x-gzip",
  "application/msword",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "application/vnd.ms-excel",
  "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  "application/vnd.ms-powerpoint",
  "application/vnd.openxmlformats-officedocument.presentationml.presentation",
]);

const MAX_SIZE = 25 * 1024 * 1024; // keep in sync with policy catalog

type UseMarkdownFileUploadParams = {
  draftId?: string;
  targetEntity?: { type: string; id: string };
};

export function useMarkdownFileUpload(params: UseMarkdownFileUploadParams) {
  const { handleAttach, getUploadedAssetIds } = useMarkdownAssetUpload<{ contentType: string }>({
    usageType: "markdown_file",
    draftId: params.draftId,
    targetEntity: params.targetEntity,
    validate: async (file) => {
      const contentType = inferContentType(file);
      if (!ALLOWED_FILE_TYPES.has(contentType)) {
        return { ok: false, message: "Этот формат файла не поддерживается" };
      }
      if (file.size > MAX_SIZE) {
        return { ok: false, message: "Максимальный размер файла: 25 МБ" };
      }
      return { ok: true, meta: { contentType } };
    },
    prepareFile: (file, { contentType }) =>
      file.type === contentType ? file : new File([file], file.name, { type: contentType }),
    buildMarkdown: (file, assetId) => buildFileMarkdown(file.name, assetId, file.size),
    uploadErrorMessage: "Не удалось загрузить файл",
  });

  return {
    handleFileAttach: handleAttach,
    getUploadedAssetIds,
  };
}
