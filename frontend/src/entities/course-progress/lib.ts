import type { CourseLearningSummaryDto } from "./types";

/**
 * Порог выдачи сертификата о прохождении курса (#650): ≥80% всех элементов программы
 * (материалы + задания + тесты) суммарно. Зеркалит backend-константу
 * `ClaimCourseCertificateHandler.COMPLETION_THRESHOLD = 0.80`.
 */
export const CERTIFICATE_COMPLETION_THRESHOLD_PERCENT = 80;

/**
 * Доступен ли студенту сертификат курса. `summary.progressPercent` приходит с бэкенда уже
 * посчитанным как `floor(completedItems / totalItems * 100)` (включает материалы, задания и
 * тесты), поэтому `progressPercent >= 80` точно совпадает с backend-gate'ом claim-эндпоинта
 * (`completedItems / totalItems >= 0.80`): фронтовая кнопка и сервер не расходятся. Пустой
 * курс (`totalItems === 0`) сертификату не подлежит.
 */
export function isCertificateEligible(summary: CourseLearningSummaryDto): boolean {
  return summary.totalItems > 0 && summary.progressPercent >= CERTIFICATE_COMPLETION_THRESHOLD_PERCENT;
}
