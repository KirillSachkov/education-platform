import { useUploadPreview } from "@/entities/file";
import { EntityTypes } from "@/shared/config/entity-types";

export function useUploadCollectionCover(collectionId: string) {
  return useUploadPreview({
    usageType: "collection_cover",
    entityType: EntityTypes.COLLECTION,
    entityId: collectionId,
    invalidateKey: ["collections", collectionId, "detail"],
    labels: {
      uploaded: "Обложка подборки загружена",
      deleted: "Обложка подборки удалена",
    },
  });
}
