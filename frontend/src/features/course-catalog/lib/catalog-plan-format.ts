import { planCoversCourse, type PublicPlanDto } from "@/entities/access-plan";
import type { CourseCatalogDto } from "@/entities/course";
import type { CourseKind } from "@/shared/config/course-kind";
import type { PlanOfferType } from "@/shared/config/offer-type";

type CoursePlanFormat = Extract<PlanOfferType, "COURSE" | "INTENSIVE" | "MARATHON">;
type CourseFormatFilter = CourseKind | "all";

export function buildPlansByCourseId(
  courses: CourseCatalogDto[],
  plans: PublicPlanDto[],
): Map<string, PublicPlanDto[]> {
  return new Map(
    courses.map((course) => [course.id, plans.filter((plan) => planCoversCourse(plan, course.id))]),
  );
}

export function buildPlanFormatByCourseId(
  courses: CourseCatalogDto[],
  plansByCourseId: Map<string, PublicPlanDto[]>,
): Map<string, CoursePlanFormat> {
  return new Map(
    courses.map((course) => [
      course.id,
      resolveCoursePlanFormat(plansByCourseId.get(course.id) ?? []),
    ]),
  );
}

/**
 * Формат курса для группировки/бейджа: смотрит на offer-type покрывающих планов.
 * INTENSIVE/MARATHON выигрывают у COURSE (приоритет INTENSIVE над MARATHON), потому
 * что определяют отдельную секцию «Интенсивы и марафоны». FULL_ACCESS-план (полный
 * доступ) не задаёт формат курса — он покрывает всё, формат берётся от course-оффера.
 */
export function resolveCoursePlanFormat(coveringPlans: PublicPlanDto[]): CoursePlanFormat {
  let hasMarathon = false;
  for (const plan of coveringPlans) {
    if (plan.offerType === "INTENSIVE") return "INTENSIVE";
    if (plan.offerType === "MARATHON") hasMarathon = true;
  }
  return hasMarathon ? "MARATHON" : "COURSE";
}

export function filterCoursesByPlanFormat(
  courses: CourseCatalogDto[],
  plans: PublicPlanDto[],
  filter: CourseFormatFilter,
): CourseCatalogDto[] {
  if (filter === "all") return courses;

  const plansByCourseId = buildPlansByCourseId(courses, plans);
  return courses.filter(
    (course) => resolveCoursePlanFormat(plansByCourseId.get(course.id) ?? []) === filter,
  );
}
