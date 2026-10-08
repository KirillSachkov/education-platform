"use client";

import { useQuery } from "@tanstack/react-query";
import { courseSlugResolveQueryOptions } from "@/entities/course";
import { CourseIdProvider } from "@/shared/providers/course-id-provider";
import { notFound } from "next/navigation";
import { useParams } from "next/navigation";
import { Loader2 } from "lucide-react";

export function AuthorCourseResolver({
  children,
}: {
  children: React.ReactNode;
}) {
  const { courseSlug } = useParams<{ courseSlug: string }>();
  const { data, isLoading, error } = useQuery(
    courseSlugResolveQueryOptions(courseSlug),
  );

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error || !data) {
    notFound();
  }

  return (
    <CourseIdProvider courseId={data.courseId} courseSlug={courseSlug}>
      {children}
    </CourseIdProvider>
  );
}
