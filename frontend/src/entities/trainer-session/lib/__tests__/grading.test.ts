import { describe, expect, it } from "vitest";
import { isGradingInProgress } from "../grading";

describe("isGradingInProgress", () => {
  it("is true while the AI grader is still working", () => {
    expect(isGradingInProgress("PENDING")).toBe(true);
    expect(isGradingInProgress("GRADING")).toBe(true);
  });

  it("is false for terminal / no-grading statuses", () => {
    expect(isGradingInProgress("NOT_REQUIRED")).toBe(false);
    expect(isGradingInProgress("GRADED")).toBe(false);
    expect(isGradingInProgress("FAILED")).toBe(false);
  });

  it("is false for missing status", () => {
    expect(isGradingInProgress(null)).toBe(false);
    expect(isGradingInProgress(undefined)).toBe(false);
  });
});
