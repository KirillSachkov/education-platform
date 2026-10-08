import { useFileUpload } from "./use-file-upload";
import { useRef } from "react";
import { toast } from "sonner";

const MAX_CONCURRENT = 3;

type UseMarkdownAssetUploadParams<TMeta> = {
  usageType: string;
  draftId?: string;
  targetEntity?: { type: string; id: string };
  /**
   * Synchronous client-side validation + metadata extraction. Return either
   * `{ ok: true, meta }` (proceed to upload, `meta` passed to `buildMarkdown`)
   * or `{ ok: false, message? }` (show optional toast and abort).
   */
  validate: (file: File) => Promise<{ ok: true; meta: TMeta } | { ok: false; message?: string }>;
  /**
   * Optional pre-upload mutation (e.g. wrap with explicit Content-Type, or
   * downscale an image). May be sync or async.
   */
  prepareFile?: (file: File, meta: TMeta) => File | Promise<File>;
  /** Build the markdown snippet inserted into the editor on success. */
  buildMarkdown: (file: File, assetId: string, meta: TMeta) => string;
  /** Toast on upload failure (post-validation, e.g. network / S3 error). */
  uploadErrorMessage: string;
};

/**
 * Shared base for `useMarkdownImageUpload` / `useMarkdownFileUpload`:
 *   - 3-slot semaphore with FIFO queue
 *   - tracks asset IDs uploaded during this form session
 *   - delegates type/size validation, file preparation, and markdown build
 *     to the caller via {@link UseMarkdownAssetUploadParams}.
 */
export function useMarkdownAssetUpload<TMeta>(params: UseMarkdownAssetUploadParams<TMeta>) {
  const uploadedAssetIdsRef = useRef(new Set<string>());
  const activeCountRef = useRef(0);
  const queueRef = useRef<Array<() => void>>([]);

  const { upload } = useFileUpload({
    usageType: params.usageType,
    draftId: params.draftId,
    targetEntity: params.targetEntity,
  });

  const acquireSlot = (): Promise<void> => {
    if (activeCountRef.current < MAX_CONCURRENT) {
      activeCountRef.current++;
      return Promise.resolve();
    }
    return new Promise<void>((resolve) => {
      queueRef.current.push(resolve);
    });
  };

  const releaseSlot = () => {
    const next = queueRef.current.shift();
    if (next) {
      next();
    } else {
      activeCountRef.current--;
    }
  };

  const handleAttach = async (file: File): Promise<string | null> => {
    const validation = await params.validate(file);
    if (!validation.ok) {
      if (validation.message) toast.error(validation.message);
      return null;
    }

    const prepared = params.prepareFile ? await params.prepareFile(file, validation.meta) : file;

    await acquireSlot();
    try {
      const response = await upload(prepared);
      uploadedAssetIdsRef.current.add(response.assetId);
      return params.buildMarkdown(file, response.assetId, validation.meta);
    } catch {
      toast.error(params.uploadErrorMessage);
      return null;
    } finally {
      releaseSlot();
    }
  };

  const getUploadedAssetIds = (): string[] => [...uploadedAssetIdsRef.current];

  return { handleAttach, getUploadedAssetIds };
}
