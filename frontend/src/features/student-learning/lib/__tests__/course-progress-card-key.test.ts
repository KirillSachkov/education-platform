import { describe, expect, it } from "vitest";
import type { UserCourseProgressDto } from "@/entities/enrollment";
import { courseProgressCardKey } from "../course-progress-card-key";

function makeCourse(overrides: Partial<UserCourseProgressDto>): UserCourseProgressDto {
  return {
    enrollmentId: "00000000-0000-0000-0000-000000000000",
    courseId: "course-a",
    courseSlug: "course-a",
    title: "Course A",
    description: "desc",
    imageId: null,
    imageUrl: null,
    totalItems: 1,
    completedItems: 0,
    totalMaterials: 1,
    completedMaterials: 0,
    totalIssues: 0,
    completedIssues: 0,
    totalModules: 0,
    completedModules: 0,
    totalQuizzes: 0,
    completedQuizzes: 0,
    progressPercent: 0,
    isNew: false,
    sortKey: "a",
    enrolledAt: "0001-01-01T00:00:00Z",
    lastActivityAt: null,
    kind: "COURSE",
    ...overrides,
  };
}

describe("courseProgressCardKey", () => {
  it("stays unique for covered courses without enrollment rows", () => {
    const courseA = makeCourse({ courseId: "course-a", courseSlug: "course-a" });
    const courseB = makeCourse({ courseId: "course-b", courseSlug: "course-b" });

    expect(courseProgressCardKey(courseA)).not.toBe(courseProgressCardKey(courseB));
  });
});
