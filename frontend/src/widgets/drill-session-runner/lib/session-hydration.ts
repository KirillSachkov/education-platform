import type { TrainerSessionItem } from "@/entities/trainer-session";

import { revealFromItem, type DrillRevealedAnswer } from "../ui/drill-answer-review";

/**
 * Seed the live `revealed` map from a session snapshot on (re)mount (#691 t4) so that
 * resuming an IN_PROGRESS session — or opening a finished one — shows the verdict +
 * correct answer of every already-answered question, read-only, instead of presenting
 * it as a fresh question. Without this, re-answering an answered item hit the server's
 * idempotency guard (409 `trainer.session.answer.already.checked`) and the wizard got
 * stuck (the auto-submit-on-navigate never resolved).
 *
 * `revealAnswered` mirrors the backend reveal-gate (`SessionMapper`):
 * `PER_QUESTION || status != IN_PROGRESS`. When it is false (a grade-at-end test still
 * IN_PROGRESS), the snapshot deliberately hides the verdict/key — those answered items
 * are tracked as blind-`submitted` instead (see {@link hydrateSubmittedAnswers}).
 */
export function hydrateRevealedAnswers(
  items: TrainerSessionItem[],
  revealAnswered: boolean,
): Record<string, DrillRevealedAnswer> {
  if (!revealAnswered) return {};
  const seeded: Record<string, DrillRevealedAnswer> = {};
  for (const item of items) {
    if (item.isAnswered) seeded[item.id] = revealFromItem(item);
  }
  return seeded;
}

/**
 * Seed the blind-`submitted` map for a grade-at-end test that is still IN_PROGRESS:
 * answered items must read as «принято» (no longer re-answerable) on resume, while their
 * verdict stays hidden until Complete.
 */
export function hydrateSubmittedAnswers(
  items: TrainerSessionItem[],
  isBlindSubmit: boolean,
): Record<string, boolean> {
  if (!isBlindSubmit) return {};
  const seeded: Record<string, boolean> = {};
  for (const item of items) {
    if (item.isAnswered) seeded[item.id] = true;
  }
  return seeded;
}

/**
 * Resolve the reveal for one question in the all-answers review (#664-E): key «answered»
 * off `item.isAnswered`, NOT off the presence of a graded verdict. An answered OPEN_TEXT
 * item carries `verdict === "PENDING"` (self-check, no score) — it must still render its
 * answer/эталон/разбор, never «Вопрос остался без ответа». Falls back to the persisted
 * snapshot when there is no live in-session reveal (resume / grade-at-end completion).
 */
export function resolveReviewReveal(
  item: TrainerSessionItem,
  liveReveal: DrillRevealedAnswer | undefined,
): DrillRevealedAnswer | null {
  if (liveReveal) return liveReveal;
  return item.isAnswered ? revealFromItem(item) : null;
}
