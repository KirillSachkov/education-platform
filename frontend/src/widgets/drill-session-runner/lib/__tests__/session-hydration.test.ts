import { describe, expect, it } from "vitest";

import type { TrainerSessionItem } from "@/entities/trainer-session";

import {
  hydrateRevealedAnswers,
  hydrateSubmittedAnswers,
  resolveReviewReveal,
} from "../session-hydration";

/**
 * #691 t4 — resume/review hydration + #664-E answered-predicate. Регрессии:
 *  - resume IN_PROGRESS-сессии показывал уже отвеченные вопросы как свежие → повтор → 409 → stuck;
 *  - completed-review помечал отвеченные (особенно OPEN_TEXT с verdict PENDING) как «без ответа».
 */
function item(overrides: Partial<TrainerSessionItem> = {}): TrainerSessionItem {
  return {
    id: "item-1",
    questionId: "q-1",
    topicId: "t-1",
    questionType: "SINGLE_CHOICE",
    questionText: "Вопрос",
    options: [
      { id: "a", text: "A" },
      { id: "b", text: "B" },
    ],
    section: null,
    difficulty: null,
    sortIndex: 0,
    isAnswered: false,
    answerRaw: null,
    scorePercent: null,
    verdict: null,
    feedback: null,
    correctOptionIds: null,
    referenceAnswer: null,
    explanation: null,
    isLocked: false,
    lockReason: null,
    ...overrides,
  };
}

const answeredChoice = item({
  id: "answered-choice",
  isAnswered: true,
  answerRaw: "a",
  scorePercent: 100,
  verdict: "CORRECT",
  correctOptionIds: ["a"],
});

const answeredOpenPending = item({
  id: "answered-open",
  questionType: "OPEN_TEXT",
  options: [],
  isAnswered: true,
  answerRaw: "мой развёрнутый ответ",
  verdict: "PENDING", // самопроверка, балла нет — это всё ещё «отвечено», не «без ответа»
  referenceAnswer: "эталон",
});

const unanswered = item({ id: "unanswered", isAnswered: false });

describe("hydrateRevealedAnswers", () => {
  it("seeds answered items when the reveal-gate is open (PER_QUESTION / finished session)", () => {
    const seeded = hydrateRevealedAnswers([answeredChoice, unanswered], true);

    expect(Object.keys(seeded)).toEqual(["answered-choice"]);
    expect(seeded["answered-choice"].verdict).toBe("CORRECT");
    expect(seeded["answered-choice"].selectedOptionIds).toEqual(["a"]);
    expect(seeded["answered-choice"].correctOptionIds).toEqual(["a"]);
  });

  it("reveals an answered OPEN_TEXT item with its PENDING self-check verdict + answer text", () => {
    const seeded = hydrateRevealedAnswers([answeredOpenPending], true);

    expect(seeded["answered-open"].verdict).toBe("PENDING");
    expect(seeded["answered-open"].textAnswer).toBe("мой развёрнутый ответ");
  });

  it("returns an empty map when the gate is closed (grade-at-end test still IN_PROGRESS)", () => {
    expect(hydrateRevealedAnswers([answeredChoice], false)).toEqual({});
  });

  it("never seeds unanswered items", () => {
    expect(hydrateRevealedAnswers([unanswered], true)).toEqual({});
  });
});

describe("hydrateSubmittedAnswers", () => {
  it("marks answered items as blind-submitted for a grade-at-end IN_PROGRESS session", () => {
    expect(hydrateSubmittedAnswers([answeredChoice, unanswered], true)).toEqual({
      "answered-choice": true,
    });
  });

  it("returns an empty map when not in blind-submit mode (PER_QUESTION / review)", () => {
    expect(hydrateSubmittedAnswers([answeredChoice], false)).toEqual({});
  });
});

describe("resolveReviewReveal (#664-E answered-predicate)", () => {
  it("prefers the live in-session reveal when present", () => {
    const live = { verdict: "INCORRECT" as const, selectedOptionIds: ["b"], textAnswer: "", correctOptionIds: ["a"], referenceAnswer: null, explanation: null, feedback: null };
    expect(resolveReviewReveal(answeredChoice, live)).toBe(live);
  });

  it("falls back to the snapshot for an answered item with no live reveal (resume / grade-at-end completion)", () => {
    const reveal = resolveReviewReveal(answeredChoice, undefined);
    expect(reveal).not.toBeNull();
    expect(reveal?.verdict).toBe("CORRECT");
  });

  it("treats an answered OPEN_TEXT (verdict PENDING) as answered, NOT «без ответа»", () => {
    const reveal = resolveReviewReveal(answeredOpenPending, undefined);
    expect(reveal).not.toBeNull();
    expect(reveal?.textAnswer).toBe("мой развёрнутый ответ");
  });

  it("returns null only for a genuinely unanswered item (renders «без ответа»)", () => {
    expect(resolveReviewReveal(unanswered, undefined)).toBeNull();
  });
});
