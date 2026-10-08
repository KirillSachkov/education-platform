import { describe, expect, it } from "vitest";

import { canRateAiFeedback, nextFeedbackRating } from "../feedback-rating";

/**
 * Гейт видимости кнопок 👍/👎 (#691 t7): рейтинг показываем ТОЛЬКО когда есть готовый AI-разбор —
 * открытый ответ оценён (вердикт не PENDING) и фидбэк присутствует. И логика оптимистичного тоггла.
 */
describe("canRateAiFeedback", () => {
  it("is true when a final verdict has a non-empty feedback", () => {
    expect(canRateAiFeedback("CORRECT", "Разбор: хорошо.")).toBe(true);
    expect(canRateAiFeedback("PARTIAL", "Не хватило примера.")).toBe(true);
    expect(canRateAiFeedback("INCORRECT", "Не по теме.")).toBe(true);
  });

  it("is false on PENDING (self-check / fallback) even with feedback", () => {
    expect(canRateAiFeedback("PENDING", "что-то")).toBe(false);
  });

  it("is false when feedback is missing or blank (auto-graded answer)", () => {
    expect(canRateAiFeedback("CORRECT", null)).toBe(false);
    expect(canRateAiFeedback("CORRECT", undefined)).toBe(false);
    expect(canRateAiFeedback("CORRECT", "   ")).toBe(false);
  });
});

describe("nextFeedbackRating", () => {
  it("sets the rating from no prior selection", () => {
    expect(nextFeedbackRating(null, "UP")).toBe("UP");
    expect(nextFeedbackRating(null, "DOWN")).toBe("DOWN");
  });

  it("toggles to the opposite rating", () => {
    expect(nextFeedbackRating("UP", "DOWN")).toBe("DOWN");
    expect(nextFeedbackRating("DOWN", "UP")).toBe("UP");
  });

  it("is a no-op (null) when re-clicking the already-active rating", () => {
    expect(nextFeedbackRating("UP", "UP")).toBeNull();
    expect(nextFeedbackRating("DOWN", "DOWN")).toBeNull();
  });
});
