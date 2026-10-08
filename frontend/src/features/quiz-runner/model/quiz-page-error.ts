import { ErrorType, isContentAccessError, isEnvelopeError, isForbiddenError } from "@/shared/api";
import type { LockReason } from "@/shared/lib/lock-copy";

export type QuizPageErrorState =
  | { kind: "not-found" }
  | { kind: "locked"; reason: LockReason }
  | { kind: "error" };

/**
 * Маппинг ошибки `GET /quizzes/{id}/student/` в состояние страницы (ST-16 #495).
 *
 * Бэкенд отдаёт: 404 — квиза нет / не опубликован; 401 анониму —
 * `content.access.unauthorized` (axios-интерсептор пробрасывает его как
 * EnvelopeError); 403 — интерсептор стирает envelope в generic ForbiddenError,
 * поэтому lock-reason восстанавливаем из auth-состояния (зеркало
 * StandaloneMaterialAccessLocked в material-view): аноним → `anonymous`
 * (CTA «Войти» с callbackUrl), авторизованный → `plan_required`.
 */
export function resolveQuizPageError(error: unknown, isAuthenticated: boolean): QuizPageErrorState {
  if (isEnvelopeError(error) && error.type === ErrorType.NOT_FOUND) {
    return { kind: "not-found" };
  }
  if (isForbiddenError(error) || isContentAccessError(error)) {
    return { kind: "locked", reason: isAuthenticated ? "plan_required" : "anonymous" };
  }
  return { kind: "error" };
}
