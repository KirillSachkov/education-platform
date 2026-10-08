import { describe, expect, it } from "vitest";

import type { AdminFeedbackRatingItem } from "@/entities/trainer-admin-stats";

import { sortByDownRate } from "../ai-feedback";

function item(overrides: Partial<AdminFeedbackRatingItem>): AdminFeedbackRatingItem {
  return {
    questionId: overrides.questionId ?? crypto.randomUUID(),
    stem: "Q",
    questionType: "OPEN_TEXT",
    difficulty: null,
    topicId: "t",
    topicTitle: "Тема",
    bankId: "b",
    up: 0,
    down: 0,
    total: 0,
    downRate: 0,
    ...overrides,
  };
}

describe("sortByDownRate", () => {
  const a = item({ questionId: "a", up: 9, down: 1, total: 10, downRate: 0.1 });
  const b = item({ questionId: "b", up: 1, down: 9, total: 10, downRate: 0.9 });
  const c = item({ questionId: "c", up: 3, down: 2, total: 5, downRate: 0.4 });

  it("puts the highest down-rate first (worst-first)", () => {
    const out = sortByDownRate([a, c, b]);
    expect(out.map((x) => x.questionId)).toEqual(["b", "c", "a"]);
  });

  it("breaks down-rate ties by total ratings (more first)", () => {
    const few = item({ questionId: "few", total: 3, downRate: 0.5 });
    const many = item({ questionId: "many", total: 40, downRate: 0.5 });
    const out = sortByDownRate([few, many]);
    expect(out.map((x) => x.questionId)).toEqual(["many", "few"]);
  });

  it("does not mutate the input array", () => {
    const input = [a, c, b];
    sortByDownRate(input);
    expect(input.map((x) => x.questionId)).toEqual(["a", "c", "b"]);
  });

  it("returns an empty array unchanged", () => {
    expect(sortByDownRate([])).toEqual([]);
  });
});
