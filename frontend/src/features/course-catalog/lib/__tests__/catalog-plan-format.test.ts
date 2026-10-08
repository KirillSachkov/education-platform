import { describe, expect, it } from "vitest";
import type { PublicPlanDto } from "@/entities/access-plan";
import type { CourseCatalogDto } from "@/entities/course";
import { filterCoursesByPlanFormat, resolveCoursePlanFormat } from "../catalog-plan-format";

function course(id: string, kind: CourseCatalogDto["kind"]): CourseCatalogDto {
  return {
    id,
    slug: id,
    title: id,
    description: "",
    kind,
    imageId: null,
    imageUrl: null,
    hasFreeContent: false,
    isNew: false,
    createdAt: "2026-01-01T00:00:00Z",
    pricing: null,
    isAccessible: false,
    showInFullAccess: true,
    authorDisplayName: null,
    authorAvatarUrl: null,
  };
}

function plan(
  id: string,
  offerType: PublicPlanDto["offerType"],
  courseIds: string[],
): PublicPlanDto {
  return {
    id,
    authorId: "author",
    tier: "COURSE",
    offerType,
    slug: id,
    displayName: id,
    shortDescription: "",
    longDescription: "",
    coverFileId: null,
    features: [],
    priceCents: 1000,
    currency: "RUB",
    discountPercent: null,
    discountEndsAt: null,
    promotionActive: false,
    effectivePriceCents: 1000,
    courseId: courseIds[0] ?? null,
    courseIds,
    includesFutureContent: false,
    trialDurationDays: null,
    capabilities: [],
    isHighlighted: false,
    termKind: "LIFETIME",
    termRecurringDays: null,
    displayOrder: 0,
    createdAt: "2026-01-01T00:00:00Z",
  };
}

describe("catalog plan format", () => {
  it("uses MARATHON offer type even when the technical course kind is INTENSIVE", () => {
    expect(resolveCoursePlanFormat([plan("marathon-plan", "MARATHON", ["marathon-course"])])).toBe(
      "MARATHON",
    );
  });

  it("filters plan-mode catalog by offer format, not by technical course kind", () => {
    const intensiveCourse = course("intensive-course", "INTENSIVE");
    const marathonCourse = course("marathon-course", "INTENSIVE");
    const courses = [intensiveCourse, marathonCourse];
    const plans = [
      plan("intensive-plan", "INTENSIVE", [intensiveCourse.id]),
      plan("marathon-plan", "MARATHON", [marathonCourse.id]),
    ];

    expect(filterCoursesByPlanFormat(courses, plans, "INTENSIVE")).toEqual([intensiveCourse]);
    expect(filterCoursesByPlanFormat(courses, plans, "MARATHON")).toEqual([marathonCourse]);
    expect(filterCoursesByPlanFormat(courses, plans, "all")).toEqual(courses);
  });
});
