"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";

/**
 * Shows a sticky banner at the top of lesson/article pages when the user
 * navigated here from an assignment (`?fromIssue={issueId}`).
 *
 * Renders nothing when the query parameter is absent, so it's safe to place
 * unconditionally above page content.
 */
export function BackToIssueBanner() {
  const courseSlug = useCourseSlug();
  const searchParams = useSearchParams();
  const fromIssue = searchParams.get("fromIssue");

  if (!fromIssue) {
    return null;
  }

  const href = routes.courseIssue(courseSlug, fromIssue);

  return (
    <div className="border-b bg-primary/5 shrink-0">
      <div className="px-3 md:px-6 py-2 flex items-center">
        <Link
          href={href}
          className="inline-flex items-center gap-1.5 text-xs font-medium text-primary hover:underline"
        >
          <ArrowLeft size={14} />
          Вернуться к заданию
        </Link>
      </div>
    </div>
  );
}
