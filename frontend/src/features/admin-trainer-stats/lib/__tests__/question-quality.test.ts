import { describe, expect, it } from "vitest";

import type { AdminQuestionQualityItem } from "@/entities/trainer-admin-stats";

import {
  defaultDirFor,
  LOW_CORRECT_RATE,
  MIN_ATTEMPTS_FOR_FLAG,
  shouldFlagRewrite,
  sortQuestions,
} from "../question-quality";

function item(overrides: Partial<AdminQuestionQualityItem>): AdminQuestionQualityItem {
  return {
    questionId: overrides.questionId ?? crypto.randomUUID(),
    stem: "Q",
    questionType: "SINGLE_CHOICE",
    difficulty: null,
    section: null,
    topicId: "t",
    topicTitle: "Тема",
    bankId: "b",
    attempts: 0,
    correctCount: 0,
    correctRate: null,
    discrimination: null,
    topGroupCorrectRate: null,
    bottomGroupCorrectRate: null,
    completedItems: 0,
    skippedItems: 0,
    skipRate: null,
    avgSecondsPerQuestion: null,
    timeSampleCount: 0,
    openText: null,
    ...overrides,
  };
}

describe("shouldFlagRewrite", () => {
  it("flags low correct-rate with enough attempts", () => {
    expect(shouldFlagRewrite({ attempts: MIN_ATTEMPTS_FOR_FLAG, correctRate: 0.2 })).toBe(true);
  });

  it("does NOT flag when attempts below threshold (small sample)", () => {
    expect(shouldFlagRewrite({ attempts: MIN_ATTEMPTS_FOR_FLAG - 1, correctRate: 0.05 })).toBe(false);
  });

  it("does NOT flag when correct-rate is at/above the low threshold", () => {
    expect(shouldFlagRewrite({ attempts: 50, correctRate: LOW_CORRECT_RATE })).toBe(false);
    expect(shouldFlagRewrite({ attempts: 50, correctRate: 0.9 })).toBe(false);
  });

  it("does NOT flag when no answers (correctRate null)", () => {
    expect(shouldFlagRewrite({ attempts: 0, correctRate: null })).toBe(false);
  });
});

describe("sortQuestions", () => {
  const a = item({ questionId: "a", correctRate: 0.9, attempts: 10 });
  const b = item({ questionId: "b", correctRate: 0.1, attempts: 30 });
  const c = item({ questionId: "c", correctRate: null, attempts: 5 });

  it("sorts ascending and pushes nulls to the end", () => {
    const out = sortQuestions([a, b, c], "correctRate", "asc");
    expect(out.map((x) => x.questionId)).toEqual(["b", "a", "c"]);
  });

  it("sorts descending and STILL pushes nulls to the end", () => {
    const out = sortQuestions([a, b, c], "correctRate", "desc");
    expect(out.map((x) => x.questionId)).toEqual(["a", "b", "c"]);
  });

  it("does not mutate the input array", () => {
    const input = [a, b, c];
    sortQuestions(input, "attempts", "desc");
    expect(input.map((x) => x.questionId)).toEqual(["a", "b", "c"]);
  });

  it("sorts by attempts descending", () => {
    const out = sortQuestions([a, b, c], "attempts", "desc");
    expect(out.map((x) => x.questionId)).toEqual(["b", "a", "c"]);
  });
});

describe("defaultDirFor", () => {
  it("surfaces worst correct/skip first (asc) and best volume/discrimination first (desc)", () => {
    expect(defaultDirFor("correctRate")).toBe("asc");
    expect(defaultDirFor("skipRate")).toBe("asc");
    expect(defaultDirFor("attempts")).toBe("desc");
    expect(defaultDirFor("discrimination")).toBe("desc");
  });
});
