import { describe, expect, it } from "vitest";
import { canAccessItem, deriveCourseAccessLevel, type CourseAccessLevel } from "../item-access";
import type { AccessType } from "@/shared/config/access-type";

// Issue #358: AccessType.FREE removed (collapsed into REGISTERED); CourseAccessLevel.free removed.

describe("canAccessItem", () => {
  const levels: CourseAccessLevel[] = ["anonymous", "authenticated", "standard", "admin"];
  const types: AccessType[] = ["PUBLIC", "REGISTERED", "ENROLLED"];

  // Expected matrix[level][type] = true/false
  const expected: Record<CourseAccessLevel, Record<AccessType, boolean>> = {
    anonymous: { PUBLIC: true, REGISTERED: false, ENROLLED: false },
    authenticated: { PUBLIC: true, REGISTERED: true, ENROLLED: false },
    standard: { PUBLIC: true, REGISTERED: true, ENROLLED: true },
    admin: { PUBLIC: true, REGISTERED: true, ENROLLED: true },
  };

  for (const level of levels) {
    for (const type of types) {
      it(`${level} + ${type} → ${expected[level][type]}`, () => {
        expect(canAccessItem(type, level)).toBe(expected[level][type]);
      });
    }
  }

  it("null accessType → false for all non-admin levels", () => {
    expect(canAccessItem(null, "anonymous")).toBe(false);
    expect(canAccessItem(null, "authenticated")).toBe(false);
    expect(canAccessItem(null, "standard")).toBe(false);
  });

  it("null accessType → true for admin (admin bypasses all)", () => {
    expect(canAccessItem(null, "admin")).toBe(true);
  });

  it("undefined accessType → false for non-admin", () => {
    expect(canAccessItem(undefined, "standard")).toBe(false);
  });
});

describe("deriveCourseAccessLevel", () => {
  it("admin bypasses enrollment", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: true,
        hasEnrollment: false,
      }),
    ).toBe("admin");
  });

  it("admin with enrollment still returns admin", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: true,
        hasEnrollment: true,
      }),
    ).toBe("admin");
  });

  it("active enrollment", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: false,
        hasEnrollment: true,
      }),
    ).toBe("standard");
  });

  it("authenticated without enrollment", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: false,
        hasEnrollment: false,
      }),
    ).toBe("authenticated");
  });

  it("course grant is standard only for included course", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: false,
        hasEnrollment: true,
        courseId: "course-1",
        authorContext: {
          highestTier: "course",
          grants: [{ plan: { tier: "COURSE", courseIds: ["course-1"] } }],
        },
      }),
    ).toBe("standard");

    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: false,
        hasEnrollment: false,
        courseId: "course-2",
        authorContext: {
          highestTier: "course",
          grants: [{ plan: { tier: "COURSE", courseIds: ["course-1"] } }],
        },
      }),
    ).toBe("authenticated");
  });

  // Regression #404: COURSE-bundle plan now carries `courseIds` (list). The course
  // can be anywhere in the bundle, not just first. A grant that covers the course
  // must unlock it. Before the fix the code read singular `plan.courseId` (gone
  // after #404) → `undefined === courseId` → every COURSE grant showed "Доступ не
  // активирован" despite a valid grant.
  it("bundle course grant unlocks a course that is not first in the set", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: true,
        isAdmin: false,
        hasEnrollment: false,
        courseId: "course-3",
        authorContext: {
          highestTier: "course",
          grants: [{ plan: { tier: "COURSE", courseIds: ["course-1", "course-2", "course-3"] } }],
        },
      }),
    ).toBe("standard");
  });

  it("anonymous", () => {
    expect(
      deriveCourseAccessLevel({
        isAuthenticated: false,
        isAdmin: false,
        hasEnrollment: false,
      }),
    ).toBe("anonymous");
  });
});
