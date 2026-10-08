"use client";

import { MaterialFeed } from "@/entities/material";
import {
  BookmarkStatusProvider,
  BookmarkToggleButton,
} from "@/entities/bookmark";

interface WhatsNewSectionProps {
  courseId: string;
}

/**
 * Course-level material feed shown on CourseHome (enrolled view).
 * Social-style vertical feed with locks and infinite scroll.
 */
export function WhatsNewSection({ courseId }: WhatsNewSectionProps) {
  return (
    <MaterialFeed
      source={{ kind: "course", courseId }}
      title="Лента курса"
      description="Недавно опубликованные материалы курса"
      renderBookmark={({ courseId, materialId, className }) => (
        <BookmarkToggleButton
          courseId={courseId}
          entityType="Material"
          entityId={materialId}
          className={className}
        />
      )}
      renderItemsWrapper={(items, children) => (
        <BookmarkStatusProvider
          items={items
            .filter((i): i is typeof i & { courseId: string } => !!i.courseId)
            .map((i) => ({
              courseId: i.courseId,
              entityType: "Material",
              entityId: i.id,
            }))}
        >
          {children}
        </BookmarkStatusProvider>
      )}
    />
  );
}
