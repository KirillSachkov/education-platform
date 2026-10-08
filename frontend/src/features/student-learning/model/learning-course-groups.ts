import type { CourseKind } from "@/shared/config/course-kind";

type LearningCourseLike = {
  kind?: CourseKind;
};

export function groupLearningCoursesByKind<T extends LearningCourseLike>(items: T[]) {
  return {
    courses: items.filter((item) => item.kind !== "INTENSIVE" && item.kind !== "MARATHON"),
    intensives: items.filter((item) => item.kind === "INTENSIVE"),
    marathons: items.filter((item) => item.kind === "MARATHON"),
  };
}
