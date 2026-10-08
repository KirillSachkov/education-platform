"use client";

import { PreviewUpload } from "@/shared/ui/components";
import { useUploadCoursePreview } from "../model/use-upload-course-preview";

type Props = {
  courseId: string;
  imageId?: string | null;
  initialPreviewUrl?: string | null;
};

export function CoursePreviewUpload({ courseId, imageId, initialPreviewUrl }: Props) {
  const hook = useUploadCoursePreview(courseId);

  return (
    <PreviewUpload
      hook={hook}
      imageId={imageId}
      initialPreviewUrl={initialPreviewUrl}
      alt="Обложка курса"
      aspectRatio="video"
    />
  );
}
