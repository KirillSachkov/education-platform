"use client";

import { CollectionDetailView } from "@/features/collection-view";
import { BookmarkToggleButton } from "@/entities/bookmark";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { routes } from "@/shared/config/routes";

interface Props {
  collectionId: string;
}

export function CourseCollectionDetailClient({ collectionId }: Props) {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();

  return (
    <CollectionDetailView
      collectionId={collectionId}
      backHref={routes.courseKnowledgeBase(courseSlug)}
      backLabel="К материалам курса"
      getMaterialHref={(materialId) =>
        routes.courseMaterial(courseSlug, materialId, {
          from: {
            kind: "collection",
            courseSlug,
            collectionId,
          },
        })
      }
      renderBookmark={({ materialId, className }) => (
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
