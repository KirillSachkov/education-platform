"use client";

import { CollectionDetailView } from "@/features/collection-view";
import { BookmarkToggleButton } from "@/entities/bookmark";
import { routes } from "@/shared/config/routes";

interface Props {
  collectionId: string;
}

export function SpaceCollectionDetailClient({ collectionId }: Props) {
  return (
    <CollectionDetailView
      collectionId={collectionId}
      backHref={routes.knowledgeBase}
      getMaterialHref={(materialId) =>
        routes.materialDetail(materialId, {
          from: {
            kind: "collection",
            collectionId,
          },
        })
      }
      renderBookmark={({ courseId, materialId, className }) => (
        <BookmarkToggleButton
          courseId={courseId}
          entityType="Material"
          entityId={materialId}
          className={className}
        />
      )}
    />
  );
}
