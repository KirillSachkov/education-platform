import type { AdminQuestionQualityItem } from "@/entities/trainer-admin-stats";

/** Сортируемые числовые метрики таблицы качества вопросов (#681 T4). */
export type QualitySortKey = "correctRate" | "discrimination" | "skipRate" | "attempts";
export type SortDir = "asc" | "desc";

/**
 * Минимум попыток, при котором аномально низкий %-верных считается сигналом «переписать»
 * (ниже — недостаточная выборка, не флажим, чтобы не гоняться за шумом).
 */
export const MIN_ATTEMPTS_FOR_FLAG = 8;
/** Порог %-верных (0..1), ниже которого вопрос — кандидат на переписывание. */
export const LOW_CORRECT_RATE = 0.4;

/**
 * Флаг «переписать»: вопрос аномально трудный (низкий %-верных) ПРИ достаточной выборке.
 * `correctRate == null` (нет ответов) или мало попыток → не флажим.
 */
export function shouldFlagRewrite(
  item: Pick<AdminQuestionQualityItem, "attempts" | "correctRate">,
  minAttempts: number = MIN_ATTEMPTS_FOR_FLAG,
  lowRate: number = LOW_CORRECT_RATE,
): boolean {
  return item.attempts >= minAttempts && item.correctRate != null && item.correctRate < lowRate;
}

/**
 * Стабильная сортировка вопросов по числовой метрике. `null`-значения (нет данных) всегда
 * уезжают В КОНЕЦ независимо от направления (их некорректно ранжировать как 0). Возвращает
 * НОВЫЙ массив (вход не мутируется — правило React Compiler).
 */
export function sortQuestions(
  items: readonly AdminQuestionQualityItem[],
  key: QualitySortKey,
  dir: SortDir,
): AdminQuestionQualityItem[] {
  const factor = dir === "asc" ? 1 : -1;
  return [...items].sort((a, b) => {
    const av = a[key];
    const bv = b[key];
    if (av == null && bv == null) return 0;
    if (av == null) return 1;
    if (bv == null) return -1;
    return (av - bv) * factor;
  });
}

/** Дефолтное направление сортировки для метрики: проблемные значения — сверху. */
export function defaultDirFor(key: QualitySortKey): SortDir {
  // %-верных и skip: меньше/больше = хуже — показываем худшее сверху.
  return key === "attempts" || key === "discrimination" ? "desc" : "asc";
}
