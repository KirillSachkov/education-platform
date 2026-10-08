"use client";

import { useSession } from "next-auth/react";
import { useQuery } from "@tanstack/react-query";
import { useRoles } from "@/shared/auth";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import type { AccessType } from "@/shared/config/access-type";
import {
  canAccessItem as canAccessItemPure,
  deriveCourseAccessLevel,
  type CourseAuthorAccessContext,
  type CourseAccessLevel,
} from "../lib/item-access";

export interface CourseAccessState {
  isAuthenticated: boolean;
  isAdmin: boolean;
  hasActiveEnrollment: boolean;
  accessLevel: CourseAccessLevel;
  /** Check if a specific item can be accessed given its accessType */
  canAccessItem: (accessType: AccessType | null | undefined) => boolean;
  isLoading: boolean;
}

export function useCourseAccess(
  courseId: string,
  opts: {
    authorContext?: CourseAuthorAccessContext | undefined;
    authorContextLoading?: boolean;
  } = {},
): CourseAccessState {
  const { status: sessionStatus } = useSession();
  const { hasRole, isAtLeast, isAuthenticated } = useRoles();
  // Tier-3 (entitlement) обходят только владелец-автор и admin. Moderator имеет Tier-2
  // (ownership) bypass, но НЕ Tier-3 — бэкенд это enforce'ит. `isAtLeast("platform-author")`
  // ошибочно включал moderator → UI показывал ENROLLED-контент навигируемым, а бэкенд 403'ил.
  // `hasRole` — точное совпадение роли, не иерархия.
  const isAdmin = hasRole("platform-author") || isAtLeast("platform-admin");

  const { data: learningState, isLoading: isLearningLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: isAuthenticated && !!courseId,
  });

  // Phase E (#45): backend больше не возвращает enrollmentStatus.
  // GetCourseLearningState отдаёт null если enrollment отсутствует/архивирован,
  // и DTO с EnrolledAt если зачисление активно.
  const hasActiveEnrollment = !!learningState;

  const accessLevel = deriveCourseAccessLevel({
    isAuthenticated,
    isAdmin,
    hasEnrollment: hasActiveEnrollment,
    courseId,
    authorContext: opts.authorContext,
  });

  const isLoading =
    sessionStatus === "loading" ||
    (isAuthenticated && !!courseId && isLearningLoading) ||
    Boolean(opts.authorContextLoading);

  return {
    isAuthenticated,
    isAdmin,
    hasActiveEnrollment,
    accessLevel,
    canAccessItem: (accessType) => canAccessItemPure(accessType, accessLevel),
    isLoading,
  };
}
