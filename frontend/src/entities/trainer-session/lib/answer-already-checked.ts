import { getErrorCode } from "@/shared/api";

/**
 * Backend conflict-code (`TrainerServiceErrors.Session.AnswerAlreadyChecked`) returned
 * with 409 when an answer is submitted for an item that is already recorded. Both
 * `CheckAnswer` and `SubmitVoiceAnswer` raise it. Mirrors the C# contract string.
 */
export const TRAINER_ANSWER_ALREADY_CHECKED_CODE = "trainer.session.answer.already.checked";

/**
 * Is this error the «answer already checked» 409? The session runner treats it as a
 * SUCCESS path (the answer IS persisted server-side) — hydrate the reveal from the
 * snapshot and let the wizard advance, instead of a blocking toast + stuck navigation
 * (the check-answer mutation never resolves its `onDone` on error).
 */
export function isAnswerAlreadyChecked(error: unknown): boolean {
  return getErrorCode(error) === TRAINER_ANSWER_ALREADY_CHECKED_CODE;
}
