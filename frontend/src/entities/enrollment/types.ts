import type { CourseKind } from "@/shared/config/course-kind";

export interface CoursePositionDto {
  entityType: "MATERIAL" | "ISSUE";
  entityId: string;
  openedAt: string;
}

export interface LastActiveCourseDto {
  enrollmentId: string;
  courseId: string;
  courseSlug: string;
  title: string;
  description: string;
  imageId: string | null;
  imageUrl: string | null;
  totalItems: number;
  completedItems: number;
  totalMaterials: number;
  completedMaterials: number;
  totalIssues: number;
  completedIssues: number;
  totalModules: number;
  completedModules: number;
  progressPercent: number;
  isNew: boolean;
  enrolledAt: string;
  lastActivityAt: string | null;
  lastPosition: CoursePositionDto | null;
  /** Optional — backend ships it in the same MR; local dev backend may not return it yet. */
  kind?: CourseKind;
}

export interface CourseEnrollmentProgressDto {
  enrollmentId: string;
  courseId: string;
  source: string;
  materialsTotal: number;
  materialsViewed: number;
  modulesTotal: number;
  modulesCompleted: number;
  issuesTotal: number;
  issuesCompleted: number;
  enrolledAt: string;
}

export interface GetMyCourseProgressRequest {
  cursor?: string;
  limit: number;
  authorId?: string;
}

export interface UserCourseProgressDto {
  enrollmentId: string;
  courseId: string;
  courseSlug: string;
  title: string;
  description: string;
  imageId: string | null;
  imageUrl: string | null;
  totalItems: number;
  completedItems: number;
  totalMaterials: number;
  completedMaterials: number;
  totalIssues: number;
  completedIssues: number;
  totalModules: number;
  completedModules: number;
  totalQuizzes: number;
  completedQuizzes: number;
  progressPercent: number;
  isNew: boolean;
  sortKey: string;
  enrolledAt: string;
  lastActivityAt: string | null;
  /** Optional — backend ships it in the same MR; local dev backend may not return it yet. */
  kind?: CourseKind;
}
