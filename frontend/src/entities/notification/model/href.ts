import type { Notification } from "./types";

/**
 * URL'ы нотификаций — single source of truth на бэке.
 *
 * NotificationService отдаёт готовый `targetUrl` в DTO. Фронт не знает структуру payload
 * по типам и не строит URL'ы. Email/TG embedded-link и proxy `/n/{id}` редиректят туда же.
 *
 * Нет ссылки → `null`: consumer открывает модалку с полным текстом
 * (`NotificationDetailDialog`), а НЕ редиректит на главную (#708).
 */
export function notificationHref(n: Notification): string | null {
  return n.targetUrl || null;
}
