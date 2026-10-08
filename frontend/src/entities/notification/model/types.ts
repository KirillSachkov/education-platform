/**
 * NotificationService DTOs.
 *
 * Backend enums (see backend/NotificationService):
 *   NotificationType — short int. Maps to platform notification types (gaps allowed:
 *   e.g. 20/TrialExpiryApproaching is backend-only and has no frontend entry).
 *   NotificationChannel — bitmask: None=0, InApp=1, Telegram=2, Email=4.
 */

export const NotificationTypes = {
  Welcome: 1,
  CourseEnrolled: 2,
  MaterialPublished: 3,
  IssueCreated: 4,
  IssueSubmissionApproved: 5,
  IssueSubmissionChangesRequested: 6,
  AuthorAnnouncement: 7,
  TelegramLinked: 8,
  IssueSubmissionAwaitingReview: 9,
  CommentReplied: 10,
  CommentOnOwnContent: 11,
  IssuePublished: 12,
  PlanGrantReceived: 13,
  AuthorHelpRequested: 14,
  PlanGrantAuthorSale: 15,
  WeeklyDigest: 16,
  AiReviewOversizedSkipped: 17,
  UserLeveledUp: 18,
  LevelTestInvite: 19,
  TelegramJoinReminder: 21,
  VideoAutoProcessingFailed: 22,
  AccessExpired: 23,
  EmailLoginNotice: 25,
  LinkAccountsNudge: 26,
  StudentPrQuestionAsked: 27,
} as const;

export type NotificationType = (typeof NotificationTypes)[keyof typeof NotificationTypes];

/**
 * Человекочитаемые метки для UI (Settings / opt-out matrix).
 * Ключ = `NotificationTypes[...]` (number).
 */
export const NotificationTypeLabels: Record<NotificationType, string> = {
  [NotificationTypes.Welcome]: "Приветствие",
  [NotificationTypes.CourseEnrolled]: "Запись на курс",
  [NotificationTypes.MaterialPublished]: "Новый материал",
  [NotificationTypes.IssueCreated]: "Новая задача",
  [NotificationTypes.IssueSubmissionApproved]: "Решение принято",
  [NotificationTypes.IssueSubmissionChangesRequested]: "Нужны правки",
  [NotificationTypes.AuthorAnnouncement]: "Объявление автора",
  [NotificationTypes.TelegramLinked]: "Telegram привязан",
  [NotificationTypes.IssueSubmissionAwaitingReview]: "Новое решение на ревью",
  [NotificationTypes.CommentReplied]: "Ответ на комментарий",
  [NotificationTypes.CommentOnOwnContent]: "Комментарий к вашему контенту",
  [NotificationTypes.IssuePublished]: "Новое задание",
  [NotificationTypes.PlanGrantReceived]: "Доступ открыт",
  [NotificationTypes.AuthorHelpRequested]: "Студент зовёт на помощь",
  [NotificationTypes.PlanGrantAuthorSale]: "Новый участник плана",
  [NotificationTypes.WeeklyDigest]: "Еженедельный дайджест",
  [NotificationTypes.AiReviewOversizedSkipped]: "Большой PR без авто-проверки",
  [NotificationTypes.UserLeveledUp]: "Повышение уровня",
  [NotificationTypes.LevelTestInvite]: "Приглашение на тест уровня",
  [NotificationTypes.TelegramJoinReminder]: "Вступление в Telegram-группу",
  [NotificationTypes.VideoAutoProcessingFailed]: "Обработка видео",
  [NotificationTypes.AccessExpired]: "Доступ истёк",
  [NotificationTypes.EmailLoginNotice]: "Вход по почте",
  [NotificationTypes.LinkAccountsNudge]: "Привязка аккаунтов",
  [NotificationTypes.StudentPrQuestionAsked]: "Вопрос студента по PR",
};

export const NotificationChannelFlags = {
  None: 0,
  InApp: 1,
  Telegram: 2,
  Email: 4,
  WebPush: 8,
} as const;

export type NotificationChannel = number; // bitmask

export interface Notification {
  id: string;
  type: NotificationType;
  templateId: string;
  title: string;
  body: string;
  /** JSON payload — contract depends on `type`; kept for UI/debug details, not route building. */
  payload: string;
  /** Resolved by NotificationService; frontend does not rebuild routes from payload. */
  targetUrl: string;
  channels: NotificationChannel;
  createdAt: string;
  readAt?: string | null;
  correlationId?: string | null;
}

/**
 * Per-user channel preferences. InApp is always on — not configurable
 * (product decision to avoid accidentally silencing the site).
 *
 * `optedOutTypes` — per-type отписка: массив short-кодов `NotificationType`, которые
 * пользователь НЕ хочет получать. Ортогонально каналам: отписка скрывает
 * уведомление во ВСЕХ каналах одновременно. Пустой массив = подписан на всё (дефолт).
 */
export interface NotificationPreference {
  telegramEnabled: boolean;
  emailEnabled: boolean;
  /**
   * Глобальный тумблер Web Push. Сами подписки устройств живут отдельно
   * (`POST /notifications/push/subscriptions/`); этот флаг — общий gate (#342).
   */
  webPushEnabled: boolean;
  optedOutTypes: number[];
}

/** Регистрация web-push подписки устройства (из браузерного `PushSubscription`). */
export interface RegisterPushSubscriptionRequest {
  endpoint: string;
  p256dh: string;
  auth: string;
  userAgent?: string;
}

/** Отписка устройства от web-push по endpoint'у. */
export interface RemovePushSubscriptionRequest {
  endpoint: string;
}

export type SubscriptionEntityType = "course" | "author" | "module";

export interface Subscription {
  id: string;
  entityType: SubscriptionEntityType;
  entityId: string;
  /** Резолвится бэкендом из ECS / AuthService. Null если lookup упал. */
  title?: string | null;
  createdAt: string;
}

export interface NotificationListResponse {
  items: Notification[];
  nextCursorBefore?: string | null;
  nextCursorId?: string | null;
}

export interface UnreadCountResponse {
  count: number;
}

export interface MarkAllAsReadResponse {
  updated: number;
}

export interface GetNotificationsRequest {
  limit?: number;
  cursorBefore?: string;
  cursorId?: string;
  unreadOnly?: boolean;
  /** Server-side фильтр по типам (short-коды NotificationType). */
  types?: NotificationType[];
}

/**
 * Preferences update request — mirrors the NotificationPreference shape.
 */
export type UpdatePreferencesRequest = NotificationPreference;

export interface CreateSubscriptionRequest {
  entityType: SubscriptionEntityType;
  entityId: string;
}

export interface NotificationCreatedEvent {
  id: string;
  type: NotificationType;
  templateId: string;
  title: string;
  body: string;
  payload: string;
  createdAt: string;
}

export interface BroadcastNotificationRequest {
  targetType: SubscriptionEntityType;
  targetId: string;
  title: string;
  body: string;
  /** Bitmask (InApp=1, Telegram=2, Email=4). Omit → системный дефолт для AuthorAnnouncement. */
  channels?: number;
}

export interface BroadcastNotificationResponse {
  broadcastId: string;
  estimatedRecipients: number;
}
