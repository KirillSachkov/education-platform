import type { AdminFeedbackRatingItem } from "@/entities/trainer-admin-stats";

/**
 * Сортировка «худшие сверху» (#691 t7): максимальный down-rate первым, при равном
 * down-rate — больше всего оценок первым (надёжнее сигнал). Возвращает НОВЫЙ массив
 * (вход не мутируется — правило React Compiler). Бэкенд уже отдаёт worst-first, но
 * фронт пересортировывает для детерминизма независимо от порядка источника.
 */
export function sortByDownRate(
  items: readonly AdminFeedbackRatingItem[],
): AdminFeedbackRatingItem[] {
  return [...items].sort((a, b) => {
    if (b.downRate !== a.downRate) return b.downRate - a.downRate;
    return b.total - a.total;
  });
}
