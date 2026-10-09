import { describe, expect, it } from "vitest";
import type { LastActiveCourseDto } from "@/entities/enrollment";
import { resolveContinueHref } from "../resolve-continue-href";

describe("continue learning", () => {
  it.each([
    [null, "/courses/devops"],
    [{ entityType: "MATERIAL", entityId: "m1" }, "/courses/devops/learn/m1"],
    [{ entityType: "ISSUE", entityId: "i1" }, "/courses/devops/issues/i1"],
  ])("keeps the purchased course context", (lastPosition, href) => {
    expect(resolveContinueHref({ courseSlug: "devops", lastPosition } as LastActiveCourseDto)).toBe(
      href,
    );
  });
});
