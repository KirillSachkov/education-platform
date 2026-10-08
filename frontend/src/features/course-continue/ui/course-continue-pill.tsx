"use client";

import { lastActiveCourseQueryOptions } from "@/entities/enrollment";
import { routes } from "@/shared/config/routes";
import { CircularProgress } from "@/shared/ui/components/circular-progress";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import Link from "next/link";

export function CourseContinuePill() {
  const { status } = useSession();
  const { data: lastCourse } = useQuery({
    ...lastActiveCourseQueryOptions(),
    enabled: status === "authenticated",
  });

  if (!lastCourse) return null;

  return (
    <Link
      href={routes.courseOverview(lastCourse.courseSlug)}
      className="group/pill relative hidden sm:flex items-center gap-2 rounded-full border border-primary/15 bg-primary/5 px-3 py-1.5 animate-soft-float transition-colors hover:bg-primary/10 hover:border-primary/30 hover:shadow-lg hover:shadow-primary/15"
    >
      <div
        className="absolute -inset-px rounded-full bg-primary/20 blur-md opacity-0 group-hover/pill:opacity-100 transition-opacity duration-300 pointer-events-none"
        aria-hidden="true"
      />
      <div className="relative flex items-center gap-2">
        <CircularProgress value={lastCourse.progressPercent} size={20} />
        <span className="text-sm font-medium">Продолжить обучение</span>
      </div>
    </Link>
  );
}
