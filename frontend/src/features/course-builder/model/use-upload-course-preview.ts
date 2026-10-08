import { EntityTypes } from "@/shared/config/entity-types";
import { useUploadPreview } from "@/entities/file";

export function useUploadCoursePreview(courseId: string) {
  return useUploadPreview({
    usageType: "course_preview",
    entityType: EntityTypes.COURSE,
    entityId: courseId,
    invalidateKey: ["courses", courseId],
    labels: {
      uploaded: "Обложка курса загружена",
      deleted: "Обложка курса удалена",
    },
  });
}
