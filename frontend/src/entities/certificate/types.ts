/**
 * Сертификат о прохождении курса. `holderName`/`courseTitle` — снапшоты на момент
 * выдачи (durable-документ: публичная страница не ходит в ECS/Auth и переживает
 * удаление курса). Issue #467.
 */
export interface CourseCertificateDto {
  id: string;
  serialNumber: string;
  holderName: string;
  courseTitle: string;
  courseId: string;
  issuedAt: string;
}
