import type { TrainerSessionHistoryItem } from "@/entities/trainer-session";

type RecencyFields = Pick<TrainerSessionHistoryItem, "completedAt" | "startedAt">;

/** Время «активности» сессии для сортировки истории: когда завершена, иначе когда начата. */
export function sessionRecencyTime(session: RecencyFields): number {
  return new Date(session.completedAt ?? session.startedAt).getTime();
}

/**
 * История симуляций — newest-first по ВРЕМЕНИ (#664). Бэкенд сортирует по `startedAt`, но карточка
 * показывает `completedAt`, из-за чего порядок выглядел «по баллу» (sort-key ≠ display-key). Сортируем
 * по ПОКАЗЫВАЕМОМУ времени (`completedAt ?? startedAt`) убыванию, чтобы прогресс читался сверху вниз.
 * Возвращает новый массив (вход не мутируется).
 */
export function sortSessionsByRecency<T extends RecencyFields>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => sessionRecencyTime(b) - sessionRecencyTime(a));
}
