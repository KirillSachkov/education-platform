export {
  authorCoursesQueryOptions,
  coursesApi,
  catalogQueryOptions,
  pendingCatalogListingInfiniteOptions,
  platformContentStatsQueryOptions,
  activePromotionQueryOptions,
  courseBuilderQueryOptions,
  courseCurriculumQueryOptions,
  courseDetailQueryOptions,
  courseLandingQueryOptions,
  courseSlugResolveQueryOptions,
  coursesQueryOptions,
} from "./api";
export { invalidateEducationContent } from "@/shared/lib/invalidate-education-content";
export { useArchiveCourse } from "./model/use-archive-course";
export { useCourseAccess, type CourseAccessState } from "./model/use-course-access";
export { usePublishCourse } from "./model/use-publish-course";
export { useRestoreCourse } from "./model/use-restore-course";
export { useToggleIsNew } from "./model/use-toggle-is-new";
export {
  getAdjacentIssues,
  getAdjacentLearningItems,
  getLearningNavigationItems,
} from "./lib/learning-navigation";
export {
  canAccessItem,
  deriveCourseAccessLevel,
  deriveLockReasonForItem,
  type CourseAuthorAccessContext,
  type CourseAccessLevel,
} from "./lib/item-access";
export { getCourseItemHref, getCourseOverviewHref } from "./lib/item-routes";
export type { AccessType } from "@/shared/config/access-type";
export type { LearningNavigationItem } from "./lib/learning-navigation";
export type {
  BuilderSectionDto,
  CourseCatalogDto,
  CourseBuilderDto,
  CourseCurriculumDto,
  CourseDetailDto,
  CourseId,
  CourseViewTab,
  CourseItemDto,
  CourseLandingDto,
  CourseLandingStatsDto,
  CourseStatus,
  CourseSummaryDto,
  CurriculumCollectionDto,
  CurriculumItemDto,
  CurriculumSectionDto,
  CreateCourseModuleRequest,
  CreateCourseProjectRequest,
  CreateCourseRequest,
  GetCatalogRequest,
  GetMyCoursesRequest,
  MoveCourseItemRequest,
  MoveCourseRequest,
  PendingCatalogCourseDto,
  PlatformContentStatsDto,
  ReassignCourseAuthorRequest,
  ReassignCourseAuthorResponse,
  UpdateCourseRequest,
} from "./types";
export { AccessGate } from "./ui/access-gate";
export { CourseCatalogCard } from "./ui/course-catalog-card";
