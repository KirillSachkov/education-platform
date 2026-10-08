import { describe, expect, it } from "vitest";
import {
  getCourseItemHref,
  getCourseOverviewHref,
} from "../item-routes";

describe("item-routes", () => {
  it("builds material route without tab", () => {
    expect(getCourseItemHref("course-1", "Material", "material-1")).toBe(
      "/courses/course-1/learn/material-1",
    );
  });

  it("builds course overview route with tab", () => {
    expect(getCourseOverviewHref("course-1", "projects")).toBe(
      "/courses/course-1?tab=projects",
    );
  });

  it("builds issue route without options", () => {
    expect(getCourseItemHref("course-1", "Issue", "issue-1")).toBe(
      "/courses/course-1/issues/issue-1",
    );
  });

  it("builds material route with fromIssue query parameter", () => {
    expect(
      getCourseItemHref("course-1", "Material", "material-1", {
        fromIssue: "issue-1",
      }),
    ).toBe("/courses/course-1/learn/material-1?fromIssue=issue-1");
  });

  it("builds issue route with fromIssue", () => {
    expect(
      getCourseItemHref("course-1", "Issue", "issue-2", {
        fromIssue: "issue-1",
      }),
    ).toBe("/courses/course-1/issues/issue-2?fromIssue=issue-1");
  });

  it("builds course quiz route", () => {
    expect(getCourseItemHref("course-1", "Quiz", "quiz-1")).toBe(
      "/courses/course-1/quiz/quiz-1",
    );
  });

  it("builds course quiz route with tab", () => {
    expect(getCourseItemHref("course-1", "Quiz", "quiz-1", { tab: "modules" })).toBe(
      "/courses/course-1/quiz/quiz-1?tab=modules",
    );
  });
});
