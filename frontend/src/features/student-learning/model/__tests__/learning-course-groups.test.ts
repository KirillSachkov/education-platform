import { describe, expect, it } from "vitest";
import { groupLearningCoursesByKind } from "../learning-course-groups";
import type { CourseKind } from "@/shared/config/course-kind";

type Item = {
  id: string;
  kind?: CourseKind;
};

describe("groupLearningCoursesByKind", () => {
  it("keeps marathon cards out of the courses group", () => {
    const groups = groupLearningCoursesByKind<Item>([
      { id: "course", kind: "COURSE" },
      { id: "intensive", kind: "INTENSIVE" },
      { id: "marathon", kind: "MARATHON" },
    ]);

    expect(groups.courses.map((item) => item.id)).toEqual(["course"]);
    expect(groups.intensives.map((item) => item.id)).toEqual(["intensive"]);
    expect(groups.marathons.map((item) => item.id)).toEqual(["marathon"]);
  });
});
