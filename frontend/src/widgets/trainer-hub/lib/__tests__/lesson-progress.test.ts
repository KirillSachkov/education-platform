import { describe, expect, it } from "vitest";
import { deriveLessonProgress } from "../lesson-progress";

describe("deriveLessonProgress", () => {
  it("untouched when no question was attempted", () => {
    const p = deriveLessonProgress({ known: 0, attempted: 0, total: 4, isComplete: false });
    expect(p).toEqual({ kind: "untouched", correct: 0, total: 4, percent: 0 });
  });

  it("untouched when the lesson has no questions", () => {
    expect(deriveLessonProgress({ known: 0, attempted: 0, total: 0, isComplete: false }).kind).toBe(
      "untouched",
    );
  });

  it("in_progress when only part of the set was answered", () => {
    const p = deriveLessonProgress({ known: 1, attempted: 2, total: 4, isComplete: false });
    expect(p.kind).toBe("in_progress");
    expect(p.correct).toBe(1);
    expect(p.percent).toBe(25);
  });

  it("attempted (last attempt X из Y) when the whole set was answered but not all correct", () => {
    const p = deriveLessonProgress({ known: 2, attempted: 4, total: 4, isComplete: false });
    expect(p).toEqual({ kind: "attempted", correct: 2, total: 4, percent: 50 });
  });

  it("attempted with 0 correct still counts as an attempt, not untouched", () => {
    const p = deriveLessonProgress({ known: 0, attempted: 4, total: 4, isComplete: false });
    expect(p.kind).toBe("attempted");
    expect(p.correct).toBe(0);
    expect(p.percent).toBe(0);
  });

  it("passed at 100%", () => {
    const p = deriveLessonProgress({ known: 4, attempted: 4, total: 4, isComplete: true });
    expect(p).toEqual({ kind: "passed", correct: 4, total: 4, percent: 100 });
  });
});
