import { describe, expect, it } from "vitest";

import { canSelfAssess } from "../self-assess";

/**
 * Гейт видимости кнопки «Не уверен» (#691 t8): мягкую самооценку показываем ТОЛЬКО на неотвеченном
 * вопросе в интерактивном PER_QUESTION-режиме. Любой из «выключающих» флагов прячет кнопку.
 */
const base = {
  isReview: false,
  isHardStopped: false,
  isGradeAtEnd: false,
  isItemChecked: false,
  isItemSubmitted: false,
  isLocked: false,
};

describe("canSelfAssess", () => {
  it("shows the button for an un-answered question in an interactive PER_QUESTION session", () => {
    expect(canSelfAssess(base)).toBe(true);
  });

  it.each([
    ["review (read-only)", { isReview: true }],
    ["MOCK timer hard-stop", { isHardStopped: true }],
    ["grade-at-end test", { isGradeAtEnd: true }],
    ["already checked", { isItemChecked: true }],
    ["blind-submitted", { isItemSubmitted: true }],
    ["locked PRO question", { isLocked: true }],
  ])("hides the button when %s", (_label, override) => {
    expect(canSelfAssess({ ...base, ...override })).toBe(false);
  });
});
