import { describe, expect, it } from "vitest";

import type { TrainerTopicMastery } from "../../types";
import { computeInterviewReadiness } from "../readiness";

/** Builds a mastery row; coverage/answers drive readiness, the rest are filler. */
function row(
  topicId: string,
  coveragePercent: number,
  answersCount: number,
  overrides: Partial<TrainerTopicMastery> = {},
): TrainerTopicMastery {
  return {
    topicId,
    masteryPercent: 100, // intentionally high — proves readiness ignores inflated EWMA mastery
    coveragePercent,
    isWeak: false,
    answersCount,
    studiedCount: answersCount,
    mistakesCount: 0,
    lastPractisedAt: "2026-06-18T09:00:00Z",
    ...overrides,
  };
}

describe("computeInterviewReadiness", () => {
  it("de-inflates readiness for 2 of 20 topics practised (breadth-weighted, not mean-over-touched)", () => {
    // Two topics deeply practised (80% coverage each) out of 20 published.
    const mastery = [row("async", 80, 6), row("sql", 80, 6)];

    const readiness = computeInterviewReadiness(mastery, 20);

    // Old formula: mean(masteryPercent over touched) = 100%. New: 160 coverage / 20 topics = 8%.
    expect(readiness.percent).toBe(8);
    expect(readiness.touchedTopics).toBe(2); // numerator of «N из M тем»
    expect(readiness.totalTopics).toBe(20); // denominator of «N из M тем»
  });

  it("ignores EWMA masteryPercent and uses coverage as the depth signal", () => {
    // masteryPercent=100 but coverage is modest → readiness reflects coverage, not mastery.
    const mastery = [row("a", 40, 5), row("b", 40, 5), row("c", 40, 5), row("d", 40, 5)];

    const readiness = computeInterviewReadiness(mastery, 4);

    // 4 × 40 = 160 over 4 topics = 40% (not the inflated 100% masteryPercent).
    expect(readiness.percent).toBe(40);
  });

  it("flags low confidence when too few topics are touched", () => {
    const readiness = computeInterviewReadiness([row("a", 90, 20), row("b", 90, 20)], 20);

    expect(readiness.touchedTopics).toBe(2); // < MIN_TOPICS_FOR_CONFIDENCE (3)
    expect(readiness.isConfident).toBe(false);
  });

  it("flags low confidence when the answer sample is too small", () => {
    // 3 topics but only 6 graded answers total (< MIN_ANSWERS_FOR_CONFIDENCE = 10).
    const mastery = [row("a", 70, 2), row("b", 70, 2), row("c", 70, 2)];

    const readiness = computeInterviewReadiness(mastery, 20);

    expect(readiness.answeredCount).toBe(6);
    expect(readiness.isConfident).toBe(false);
  });

  it("is confident with enough breadth and answers", () => {
    const mastery = [row("a", 60, 4), row("b", 60, 4), row("c", 60, 4)];

    const readiness = computeInterviewReadiness(mastery, 20);

    expect(readiness.touchedTopics).toBe(3);
    expect(readiness.answeredCount).toBe(12);
    expect(readiness.isConfident).toBe(true);
  });

  it("reports high readiness on full-coverage high-mastery", () => {
    const mastery = Array.from({ length: 20 }, (_, i) => row(`t${i}`, 90, 10));

    const readiness = computeInterviewReadiness(mastery, 20);

    expect(readiness.percent).toBe(90); // 20 × 90 / 20 = 90
    expect(readiness.touchedTopics).toBe(20);
    expect(readiness.totalTopics).toBe(20);
    expect(readiness.isConfident).toBe(true);
  });

  it("returns null percent when nothing has been practised", () => {
    expect(computeInterviewReadiness([], 20)).toMatchObject({
      percent: null,
      touchedTopics: 0,
      totalTopics: 20,
      isConfident: false,
    });

    // Rows with zero answers are not 'practised' — they must not lift readiness.
    const untouched = [row("a", 0, 0), row("b", 0, 0)];
    expect(computeInterviewReadiness(untouched, 20).percent).toBeNull();
    expect(computeInterviewReadiness(untouched, 20).touchedTopics).toBe(0);
  });

  it("never exceeds 100% when the published count lags behind practised rows", () => {
    // Stale/low totalPublishedTopics → denominator clamps up to touched count, no >100%.
    const mastery = [row("a", 100, 8), row("b", 100, 8), row("c", 100, 8)];

    const readiness = computeInterviewReadiness(mastery, 1);

    expect(readiness.totalTopics).toBe(3);
    expect(readiness.percent).toBe(100);
  });
});
