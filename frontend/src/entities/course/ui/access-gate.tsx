"use client";

import type { ReactNode } from "react";
import type { AccessType } from "@/shared/config/access-type";
import { useCourseAccess } from "../model/use-course-access";

interface AccessGateProps {
  courseId: string;
  accessType: AccessType | null | undefined;
  children: ReactNode;
  fallback?: ReactNode;
  /** Pre-computed function from useCourseAccess — avoids redundant hook call */
  canAccess?: (accessType: AccessType | null | undefined) => boolean;
}

export function AccessGate({
  courseId,
  accessType,
  children,
  fallback = null,
  canAccess,
}: AccessGateProps) {
  // Hook must be called unconditionally (React rules). When canAccess is provided
  // the hook result is unused — zero extra cost due to React Query cache sharing.
  const access = useCourseAccess(courseId);
  const check = canAccess ?? access.canAccessItem;

  return check(accessType) ? <>{children}</> : <>{fallback}</>;
}
