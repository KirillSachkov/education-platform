import { fileApi } from "../api";
import { extractAssetIds } from "@/shared/lib/markdown-assets";

type BindParams = {
  targetEntity: { type: string; id: string };
  markdownText: string;
} & ({ mode: "create"; draftId: string } | { mode: "edit" });

export async function bindMarkdownAssets(params: BindParams): Promise<void> {
  const assetIds = extractAssetIds(params.markdownText);

  if (params.mode === "create") {
    if (assetIds.length === 0 && !params.draftId) return;

    await fileApi.bindDraftAssets({
      draftId: params.draftId,
      targetEntity: params.targetEntity,
      assetIds,
    });
  } else {
    await fileApi.syncEntityAssets({
      targetEntity: params.targetEntity,
      usageTypes: ["markdown_image", "markdown_file"],
      activeAssetIds: assetIds,
    });
  }
}
