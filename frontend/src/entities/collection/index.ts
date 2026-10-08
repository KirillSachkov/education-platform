export {
  collectionsApi,
  collectionDetailQueryOptions,
  myCollectionsQueryOptions,
  authorCollectionsQueryOptions,
  courseCollectionsQueryOptions,
  collectionsQueryOptions,
} from "./api";
export {
  getAdjacentCollectionMaterials,
  getCollectionNavigationItems,
  type CollectionNavigationItem,
} from "./lib/collection-navigation";
export { CollectionCard, getCollectionGradient } from "./ui/collection-card";
export { CollectionCoverImage } from "./ui/collection-cover-image";
export { CollectionGrid } from "./ui/collection-grid";
export type {
  CollectionAccessType,
  CollectionDetailDto,
  CollectionId,
  CollectionItemDto,
  CollectionItemType,
  CollectionLockReason,
  CollectionSectionDto,
  CollectionStatus,
  CollectionSummaryDto,
  CreateCollectionRequest,
  UpdateCollectionRequest,
  UpdateSectionRequest,
  AddSectionRequest,
  AddItemRequest,
  BulkSetItemsAccessTypeResponse,
} from "./types";
