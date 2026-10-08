import { describe, expect, it } from "vitest";

import { EnvelopeError } from "@/shared/api";

import {
  isAnswerAlreadyChecked,
  TRAINER_ANSWER_ALREADY_CHECKED_CODE,
} from "../answer-already-checked";

/**
 * #691 t4 — 409 «ответ уже проверен» как success-path. Раннер не должен показывать
 * блокирующий тост и застревать: ответ уже сохранён сервером. Эта проверка решает,
 * относиться ли к ошибке как к «уже отвечено» (тогда раскрываем из снапшота + идём дальше)
 * или как к настоящему сбою.
 */
function envelopeError(code: string): EnvelopeError {
  return new EnvelopeError({
    messages: [{ code, message: "Ответ на этот вопрос уже проверен." }],
    type: "CONFLICT",
  });
}

describe("isAnswerAlreadyChecked", () => {
  it("mirrors the backend conflict code constant", () => {
    expect(TRAINER_ANSWER_ALREADY_CHECKED_CODE).toBe("trainer.session.answer.already.checked");
  });

  it("is true for the already-checked 409 envelope error", () => {
    expect(isAnswerAlreadyChecked(envelopeError(TRAINER_ANSWER_ALREADY_CHECKED_CODE))).toBe(true);
  });

  it("is false for a different trainer error code (still surfaces a toast)", () => {
    expect(isAnswerAlreadyChecked(envelopeError("trainer.pro.required"))).toBe(false);
    expect(isAnswerAlreadyChecked(envelopeError("trainer.session.already.completed"))).toBe(false);
  });

  it("is false for non-envelope errors", () => {
    expect(isAnswerAlreadyChecked(new Error("network down"))).toBe(false);
    expect(isAnswerAlreadyChecked(null)).toBe(false);
    expect(isAnswerAlreadyChecked(undefined)).toBe(false);
  });
});
