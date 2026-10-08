/**
 * DTOs для admin кампаний-рассылок (NotificationService.Contracts.Admin.Dtos).
 *
 * Кампании (все endpoint'ы — permission Platform.ADMIN, идемпотентны per-user):
 * - `level-test-invite` (#554) — приглашение ВСЕМ пользователям пройти тест уровня
 *   (InApp + Email);
 * - `email-login-notice` (#704) — «вход теперь по почте» пользователям С GitHub-привязкой
 *   (InApp + форсированный Email — критичное уведомление об аккаунте);
 * - `link-accounts-nudge` (#704) — «привяжите GitHub и Telegram» пользователям БЕЗ
 *   GitHub-привязки (только InApp).
 */

/** Слаг кампании — сегмент пути `/notifications/admin/campaigns/{slug}/...`. */
export type AdminCampaignSlug = "level-test-invite" | "email-login-notice" | "link-accounts-nudge";

/** Размер адресуемой аудитории (до opt-out / канальной фильтрации на доставке). */
export interface CampaignRecipientCountResponse {
  count: number;
}

/** Результат тестовой отправки уведомления самому админу. */
export interface SendTestCampaignResponse {
  sent: boolean;
}

/** Результат запуска кампании — скольким поставлено в очередь в этом проходе. */
export interface RunCampaignResponse {
  queued: number;
}
