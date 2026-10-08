import { NotificationTypes, type NotificationType } from "./types";

/**
 * Категории нотификаций для UI-фильтрации в инбоксе и author-feed'е.
 *
 * Источник правды (бэк) — `NotificationType` enum. Категория — frontend-only
 * группировка для tab-фильтра, ничего не меняет в payload или dispatch.
 *
 * При добавлении нового типа в `NotificationTypes`:
 * 1) добавь соответствующую запись в `NOTIFICATION_TYPE_CATEGORY` ниже,
 * 2) если открывается новая семантика (например «биллинг») — добавь категорию.
 */
export const NotificationCategories = {
  Course: "course",
  Submissions: "submissions",
  Comments: "comments",
  Account: "account",
} as const;

export type NotificationCategory =
  (typeof NotificationCategories)[keyof typeof NotificationCategories];

export const NotificationCategoryLabels: Record<NotificationCategory, string> = {
  [NotificationCategories.Course]: "Курсы",
  [NotificationCategories.Submissions]: "Задания",
  [NotificationCategories.Comments]: "Комментарии",
  [NotificationCategories.Account]: "Аккаунт",
};

/**
 * Mapping тип → категория. Полный (закрывает все типы) — иначе при появлении
 * нового типа без mapping он не попадёт ни в один таб.
 */
const NOTIFICATION_TYPE_CATEGORY: Record<NotificationType, NotificationCategory> = {
  [NotificationTypes.Welcome]: NotificationCategories.Account,
  [NotificationTypes.CourseEnrolled]: NotificationCategories.Course,
  [NotificationTypes.MaterialPublished]: NotificationCategories.Course,
  [NotificationTypes.IssueCreated]: NotificationCategories.Submissions,
  [NotificationTypes.IssueSubmissionApproved]: NotificationCategories.Submissions,
  [NotificationTypes.IssueSubmissionChangesRequested]: NotificationCategories.Submissions,
  [NotificationTypes.IssueSubmissionAwaitingReview]: NotificationCategories.Submissions,
  [NotificationTypes.AuthorAnnouncement]: NotificationCategories.Course,
  [NotificationTypes.TelegramLinked]: NotificationCategories.Account,
  [NotificationTypes.CommentReplied]: NotificationCategories.Comments,
  [NotificationTypes.CommentOnOwnContent]: NotificationCategories.Comments,
  // Новое опубликованное задание подписчикам курса — как IssueCreated, «Задания».
  [NotificationTypes.IssuePublished]: NotificationCategories.Submissions,
  // Доступ по плану открыт (#485) — учебное событие, юзер ищет его рядом с курсами.
  [NotificationTypes.PlanGrantReceived]: NotificationCategories.Course,
  // «Позвать автора» (#383) — author-facing review-сигнал, рядом с остальными ревью.
  [NotificationTypes.AuthorHelpRequested]: NotificationCategories.Submissions,
  // Продажа/новый участник плана — author-facing, кладём в «Аккаунт» (отдельной
  // billing-категории пока нет; уведомление всё равно видно в «Все» + инбоксе).
  [NotificationTypes.PlanGrantAuthorSale]: NotificationCategories.Account,
  // Еженедельный дайджест «что нового за неделю» (#468) — агрегат course-новостей
  // (материалы + задания), поэтому категория «Курсы».
  [NotificationTypes.WeeklyDigest]: NotificationCategories.Course,
  // Большой PR без авто-проверки (#546) — author-facing review-сигнал, как AuthorHelpRequested.
  [NotificationTypes.AiReviewOversizedSkipped]: NotificationCategories.Submissions,
  // Повышение уровня (#555) — личное достижение пользователя, категория «Аккаунт».
  [NotificationTypes.UserLeveledUp]: NotificationCategories.Account,
  // Приглашение пройти публичный тест уровня (#554) — учебная воронка, ведёт в курсы.
  [NotificationTypes.LevelTestInvite]: NotificationCategories.Course,
  // Напоминание вступить в Telegram-группу плана (#616) — про Telegram-аккаунт и
  // доступ, рядом с TelegramLinked в «Аккаунт».
  [NotificationTypes.TelegramJoinReminder]: NotificationCategories.Account,
  // Авто-обработка загруженного видео упала (#648) — author-facing сигнал по
  // материалу/курсу, рядом с остальными content-событиями в «Курсы».
  [NotificationTypes.VideoAutoProcessingFailed]: NotificationCategories.Course,
  // Срок доступа истёк (#687) — про доступ к курсам/контенту, рядом с PlanGrantReceived.
  [NotificationTypes.AccessExpired]: NotificationCategories.Course,
  // «Вход теперь по почте» (#704) — критичное уведомление об аккаунте (GitHub-вход
  // отключён законом), рядом с Welcome/TelegramLinked в «Аккаунт».
  [NotificationTypes.EmailLoginNotice]: NotificationCategories.Account,
  // «Привяжите GitHub и Telegram» (#704) — про привязки аккаунта, категория «Аккаунт».
  [NotificationTypes.LinkAccountsNudge]: NotificationCategories.Account,
  // Вопрос студента в PR (#713) — author-facing review-сигнал, как AuthorHelpRequested.
  [NotificationTypes.StudentPrQuestionAsked]: NotificationCategories.Submissions,
};

export function categoryOf(type: NotificationType): NotificationCategory {
  return NOTIFICATION_TYPE_CATEGORY[type];
}

/**
 * Все типы, относящиеся к указанной категории. Используется для server-side
 * фильтрации `?types=…` в инбоксе и author-comments-feed'е.
 */
export function typesInCategory(category: NotificationCategory): NotificationType[] {
  const result: NotificationType[] = [];
  for (const [typeStr, cat] of Object.entries(NOTIFICATION_TYPE_CATEGORY)) {
    if (cat === category) result.push(Number(typeStr) as NotificationType);
  }
  return result;
}

/** Типы, относящиеся к комментариям (используется author-feed'ом «Комментарии»). */
export const COMMENT_NOTIFICATION_TYPES: NotificationType[] = [
  NotificationTypes.CommentReplied,
  NotificationTypes.CommentOnOwnContent,
];
