"use client";

import { useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { courseSlugResolveQueryOptions } from "@/entities/course";
import { CourseIdProvider } from "@/shared/providers/course-id-provider";
import { AppLayout } from "./app-layout";
import { CourseBuilderSidebar } from "@/widgets/sidebar";

/**
 * Shell for author sub-pages (materials, collections) that can be entered from
 * a course. When `?courseSlug=X` is present, swaps the default AppSidebar for
 * the CourseBuilderSidebar so the user stays anchored in course context.
 */
export function AuthorContentShell({ children }: { children: React.ReactNode }) {
  const searchParams = useSearchParams();
  const courseSlug = searchParams.get("courseSlug");

  if (courseSlug) {
    return (
      <CourseContextLayout courseSlug={courseSlug}>{children}</CourseContextLayout>
    );
  }

  return <AppLayout>{children}</AppLayout>;
}

function CourseContextLayout({
  courseSlug,
  children,
}: {
  courseSlug: string;
  children: React.ReactNode;
}) {
  const { data, isLoading } = useQuery(courseSlugResolveQueryOptions(courseSlug));

  if (isLoading) {
    return (
      <AppLayout>
        <div className="flex h-full items-center justify-center">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      </AppLayout>
    );
  }

  // If slug resolution fails, degrade gracefully to default sidebar.
  if (!data) {
    return <AppLayout>{children}</AppLayout>;
  }

  return (
    <CourseIdProvider courseId={data.courseId} courseSlug={courseSlug}>
      <AppLayout sidebar={<CourseBuilderSidebar />}>{children}</AppLayout>
    </CourseIdProvider>
  );
}
