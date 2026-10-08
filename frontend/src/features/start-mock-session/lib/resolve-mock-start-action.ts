/** Что делать по клику «Начать симуляцию» — чистое решение для тестируемости (#658). */
export type MockStartAction = "unavailable" | "login" | "paywall" | "busy" | "start";

export interface MockStartInput {
  /** Есть ли у выбранного собеса доступные вопросы (бэкенд считает резолвимые). */
  isAvailable: boolean;
  /** Залогинен ли вызывающий. */
  isAuthenticated: boolean;
  /** Есть ли Trainer Pro. `undefined` — статус ещё не загружен (пропускаем pre-check). */
  hasPro: boolean | undefined;
  /** Идёт ли уже старт-мутация (анти-дабл-клик). */
  isPending: boolean;
}

/**
 * Приоритет веток клика «Начать»: нет вопросов → no-op; аноним → логин; не-Pro →
 * пейволл (#658); уже стартуем → no-op; иначе старт. `hasPro === undefined` (ещё
 * грузится) НЕ блокирует — падаем в `start`, сервер всё равно гейтит 403-ом.
 */
export function resolveMockStartAction({
  isAvailable,
  isAuthenticated,
  hasPro,
  isPending,
}: MockStartInput): MockStartAction {
  if (!isAvailable) return "unavailable";
  if (!isAuthenticated) return "login";
  if (hasPro === false) return "paywall";
  if (isPending) return "busy";
  return "start";
}
