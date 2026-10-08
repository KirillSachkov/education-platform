"use client";

import { BookmarksList } from "@/features/bookmarks-list";
import { useCourseId } from "@/shared/providers/course-id-provider";

export default function CourseBookmarksPage() {
  const courseId = useCourseId();

  return (
    <div className="p-6">
      <h1 className="text-xl font-semibold mb-6">Закладки курса</h1>
      <BookmarksList courseId={courseId} />
    </div>
  );
}
