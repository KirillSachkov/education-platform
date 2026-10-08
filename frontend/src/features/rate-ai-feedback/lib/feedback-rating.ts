import type { TrainerFeedbackRating, TrainerVerdict } from "@/entities/trainer-session";

/**
 * Можно ли оценить AI-разбор этого ответа (#691 t7). Кнопки 👍/👎 показываем ТОЛЬКО когда есть
 * готовый «Разбор ИИ»: открытый ответ уже оценён (вердикт не `PENDING`) и фидбэк-разбор присутствует.
 * Зеркалит гейт блока `AiFeedback` — на самопроверке (PENDING) / авто-грейде разбора нет, нечего оценивать.
 */
export function canRateAiFeedback(
  verdict: TrainerVerdict,
  feedback: string | null | undefined,
): boolean {
  return verdict !== "PENDING" && !!feedback && feedback.trim().length > 0;
}

/**
 * Оптимистичное переключение оценки по клику (#691 t7): клик по той же кнопке оставляет её активной
 * (бэкенд не умеет «снять» оценку — повтор = no-op), клик по противоположной переключает UP↔DOWN.
 * Возвращает `null`, если посылать запрос не нужно (кликнули уже активную — чистый no-op).
 */
export function nextFeedbackRating(
  current: TrainerFeedbackRating | null,
  clicked: TrainerFeedbackRating,
): TrainerFeedbackRating | null {
  return current === clicked ? null : clicked;
}
