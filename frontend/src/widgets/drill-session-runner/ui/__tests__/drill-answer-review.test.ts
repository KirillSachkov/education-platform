import { describe, expect, it } from "vitest";

import type { CheckAnswerResponse, TrainerSessionItem } from "@/entities/trainer-session";

import { revealFromItem, revealFromResponse } from "../drill-answer-review";

/**
 * Маппинг ответа сервера в разбор (#585). Регрессия: голосовой OPEN_TEXT-ответ показывался пустым
 * («Твой ответ —»), а AI-фидбэк терялся. Теперь «Твой ответ» берём из `response.answerText`
 * (распознанная речь), а `feedback` прокидывается в блок «Разбор ИИ».
 */
function response(overrides: Partial<CheckAnswerResponse> = {}): CheckAnswerResponse {
  return {
    itemId: "item-1",
    verdict: "CORRECT",
    scorePercent: 100,
    correctOptionIds: null,
    referenceAnswer: "Эталон",
    explanation: "Пояснение",
    feedback: "Раскрыта суть.",
    answerText: null,
    ...overrides,
  };
}

function item(overrides: Partial<TrainerSessionItem> = {}): TrainerSessionItem {
  return {
    id: "item-1",
    questionId: "q-1",
    topicId: "t-1",
    questionType: "OPEN_TEXT",
    questionText: "Что такое record в C#?",
    options: [],
    section: null,
    difficulty: null,
    sortIndex: 0,
    isAnswered: true,
    answerRaw: "record — ссылочный тип с value-равенством",
    scorePercent: 100,
    verdict: "CORRECT",
    feedback: "Хороший ответ.",
    correctOptionIds: null,
    referenceAnswer: "Эталон",
    explanation: "Пояснение",
    isLocked: false,
    lockReason: null,
    ...overrides,
  };
}

describe("revealFromResponse", () => {
  it("uses the server transcript as «Твой ответ» for a voice answer (local draft is empty)", () => {
    const revealed = revealFromResponse(
      response({ answerText: "Запись — ссылочный тип со value-равенством" }),
      { selectedOptionIds: [], textAnswer: "" },
    );

    expect(revealed.textAnswer).toBe("Запись — ссылочный тип со value-равенством");
    expect(revealed.verdict).toBe("CORRECT");
    expect(revealed.feedback).toBe("Раскрыта суть.");
    expect(revealed.referenceAnswer).toBe("Эталон");
  });

  it("falls back to the local draft text when the server returns no answerText (typed answer)", () => {
    const revealed = revealFromResponse(response({ answerText: null }), {
      selectedOptionIds: [],
      textAnswer: "мой печатный ответ",
    });

    expect(revealed.textAnswer).toBe("мой печатный ответ");
  });

  it("threads the AI feedback through so «Разбор ИИ» can render it", () => {
    const revealed = revealFromResponse(response({ feedback: "Чего не хватило: примера." }), {
      selectedOptionIds: [],
      textAnswer: "x",
    });

    expect(revealed.feedback).toBe("Чего не хватило: примера.");
  });

  it("keeps feedback null when the grade was unavailable (PENDING fallback)", () => {
    const revealed = revealFromResponse(
      response({ verdict: "PENDING", scorePercent: null, feedback: null, answerText: "распознанный текст" }),
      { selectedOptionIds: [], textAnswer: "" },
    );

    // The transcript is still surfaced even on the PENDING self-check fallback.
    expect(revealed.textAnswer).toBe("распознанный текст");
    expect(revealed.feedback).toBeNull();
    expect(revealed.verdict).toBe("PENDING");
  });

  it("carries choice selections + correct ids for an auto-graded answer", () => {
    const revealed = revealFromResponse(
      response({ correctOptionIds: ["a", "b"], referenceAnswer: null, feedback: null, answerText: null }),
      { selectedOptionIds: ["a"], textAnswer: "" },
    );

    expect(revealed.selectedOptionIds).toEqual(["a"]);
    expect(revealed.correctOptionIds).toEqual(["a", "b"]);
  });
});

describe("revealFromItem", () => {
  it("maps the persisted item incl. AI feedback for the completed-session review", () => {
    const revealed = revealFromItem(item());

    expect(revealed.verdict).toBe("CORRECT");
    expect(revealed.textAnswer).toBe("record — ссылочный тип с value-равенством");
    expect(revealed.feedback).toBe("Хороший ответ.");
    expect(revealed.referenceAnswer).toBe("Эталон");
  });

  it("defaults verdict to PENDING and feedback stays null for an item without them", () => {
    const revealed = revealFromItem(item({ verdict: null, feedback: null }));

    expect(revealed.verdict).toBe("PENDING");
    expect(revealed.feedback).toBeNull();
  });
});
