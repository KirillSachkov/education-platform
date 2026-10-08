import { describe, expect, it } from "vitest";
import { shouldAutoSubmitOnLeave } from "../should-auto-submit";

const base = {
  isReview: false,
  isHardStopped: false,
  isItemRecorded: false,
  isAnswerPending: false,
  hasAnswerDraft: true,
};

describe("shouldAutoSubmitOnLeave", () => {
  it("saves a selected answer when leaving a question (any mode: test / training / mistakes / mock)", () => {
    expect(shouldAutoSubmitOnLeave(base)).toBe(true);
  });

  it("does not save when there is no answer draft (skipping a question)", () => {
    expect(shouldAutoSubmitOnLeave({ ...base, hasAnswerDraft: false })).toBe(false);
  });

  it("does not re-save an already recorded answer (checked or accepted) — avoids a 409", () => {
    expect(shouldAutoSubmitOnLeave({ ...base, isItemRecorded: true })).toBe(false);
  });

  it("does not save while a submit is already pending", () => {
    expect(shouldAutoSubmitOnLeave({ ...base, isAnswerPending: true })).toBe(false);
  });

  it("does not save in a completed (review) session", () => {
    expect(shouldAutoSubmitOnLeave({ ...base, isReview: true })).toBe(false);
  });

  it("does not save after the timer hard-stops the simulation", () => {
    expect(shouldAutoSubmitOnLeave({ ...base, isHardStopped: true })).toBe(false);
  });
});
