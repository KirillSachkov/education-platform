"use client";

import { createContext, use } from "react";

const CourseContext = createContext<{
  courseId: string;
  courseSlug: string;
} | null>(null);

export function CourseIdProvider({
  courseId,
  courseSlug,
  children,
}: {
  courseId: string;
  courseSlug: string;
  children: React.ReactNode;
}) {
  return (
    <CourseContext value={{ courseId, courseSlug }}>{children}</CourseContext>
  );
}

export function useCourseId(): string {
  const ctx = use(CourseContext);
  if (!ctx) throw new Error("useCourseId must be used within CourseIdProvider");
  return ctx.courseId;
}

export function useCourseSlug(): string {
  const ctx = use(CourseContext);
  if (!ctx)
    throw new Error("useCourseSlug must be used within CourseIdProvider");
  return ctx.courseSlug;
}

/** Returns { courseId, courseSlug } if inside CourseIdProvider, null otherwise. */
export function useCourseContext(): {
  courseId: string;
  courseSlug: string;
} | null {
  return use(CourseContext);
}
