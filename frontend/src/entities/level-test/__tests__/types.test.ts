import { describe, expect, it } from "vitest";
import {
  isAiGradingPending,
  isFullLevelTestResult,
  type LevelTestAttemptResultDto,
  type LevelTestAttemptTeaserDto,
} from "../types";

const TEASER: LevelTestAttemptTeaserDto = {
  attemptId: "a-1",
  overallPercent: 61,
  level: "MIDDLE",
  totalQuestions: 22,
  answeredCount: 20,
  aiGradingStatus: "QUEUED",
};

const FULL: LevelTestAttemptResultDto = {
  attemptId: "a-1",
  quizId: "q-1",
  overallPercent: 61,
  level: "MIDDLE",
  aiGradingStatus: "READY",
  recommendedCourseId: null,
  sections: [],
  questions: [],
  weakestSectionKeys: [],
};

describe("isFullLevelTestResult", () => {
  it("teaser (no sections field in JSON) is not a full result", () => {
    expect(isFullLevelTestResult(TEASER)).toBe(false);
  });

  it("full result is detected by the physical sections array", () => {
    expect(isFullLevelTestResult(FULL)).toBe(true);
  });
});

describe("isAiGradingPending", () => {
  it("QUEUED and GRADING keep the poll running", () => {
    expect(isAiGradingPending("QUEUED")).toBe(true);
    expect(isAiGradingPending("GRADING")).toBe(true);
  });

  it("NONE, READY and FAILED stop the poll", () => {
    expect(isAiGradingPending("NONE")).toBe(false);
    expect(isAiGradingPending("READY")).toBe(false);
    expect(isAiGradingPending("FAILED")).toBe(false);
  });
});
