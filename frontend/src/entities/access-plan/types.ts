/**
 * Phase 1.3 #111: PlanTier — single source-of-truth для типа плана.
 *
 * - `FREE` — legacy (archived after #358). Новые планы этого тира не создаются;
 *   значение остаётся для defensive рендера существующих архивных строк в БД.
 * - `LEARN_ALL` — все материалы платформы (только просмотр, без practice/community).
 * - `FULL_ALL` — полный доступ ко всему контенту платформы.
 * - `COURSE` — план на конкретный курс или подборку (capabilities на выбор).
 * - `SUBSCRIPTION` — dormant до payment provider integration.
 *
 * Singleton-tier'ы (LEARN_ALL/FULL_ALL) — один active+public на платформу.
 */
import type { CourseKind } from "@/shared/config/course-kind";
import type { PlanOfferType } from "@/shared/config/offer-type";

export type PlanTier = "FREE" | "LEARN_ALL" | "FULL_ALL" | "COURSE" | "SUBSCRIPTION";

/**
 * Имена флагов из C# `[Flags] enum PlanCapabilities`. Backend возвращает массив
 * имён через canonical mapping (не Enum.ToString — там алиасы FULL/LEARN_ONLY).
 */
export type PlanCapability =
  | "VIEW_MATERIALS"
  | "SUBMIT_ISSUES"
  | "CODE_REVIEW"
  | "COMMUNITY_ACCESS"
  | "LIVE_CALLS"
  | "JOB_SUPPORT"
  // Trainer Pro (#614) — выдаётся подписочным планом (SUBSCRIPTION), не входит в FULL
  // и не редактируется автором вручную, поэтому НЕ включён в ALL_PLAN_CAPABILITIES.
  | "TRAINER_PRO";

export const ALL_PLAN_CAPABILITIES: ReadonlyArray<PlanCapability> = [
  "VIEW_MATERIALS",
  "SUBMIT_ISSUES",
  "CODE_REVIEW",
  "COMMUNITY_ACCESS",
  "LIVE_CALLS",
  "JOB_SUPPORT",
];

export const PLAN_CAPABILITY_LABELS: Record<PlanCapability, string> = {
  VIEW_MATERIALS: "Просмотр материалов",
  SUBMIT_ISSUES: "Отправка решений",
  CODE_REVIEW: "AI-ревью PR",
  COMMUNITY_ACCESS: "Закрытое сообщество",
  LIVE_CALLS: "Еженедельные созвоны",
  JOB_SUPPORT: "Помощь с резюме и поиском работы",
  TRAINER_PRO: "Тренажёр Pro",
};

export const PLAN_CAPABILITY_DESCRIPTIONS: Record<PlanCapability, string> = {
  VIEW_MATERIALS: "Доступ к чтению/просмотру материалов курсов плана.",
  SUBMIT_ISSUES: "Можно отправлять решения практических заданий на проверку.",
  CODE_REVIEW: "Автоматическое AI-ревью решений, отправленных через pull request.",
  COMMUNITY_ACCESS: "Закрытый чат / Telegram-комьюнити для участников плана.",
  LIVE_CALLS: "Регулярные онлайн-встречи с автором.",
  JOB_SUPPORT: "Консультации по резюме и поиску работы по запросу.",
  TRAINER_PRO: "Расширенный тренажёр собеседований: голос, мок-интервью, без лимитов.",
};

export type PlanGrantSource =
  | "INVITE_LINK"
  | "ADMIN_GRANT"
  | "MIGRATION"
  | "PURCHASE"
  | "TRIAL"
  | "GITHUB_ORG"
  | "TELEGRAM_F1"
  | "AUTO_FREE";

export type PlanGrantStatus = "ACTIVE" | "REVOKED" | "EXPIRED";

export interface PlanDto {
  id: string;
  authorId: string;
  tier: PlanTier;
  /** Маркетинг-формат оффера — ортогонален tier'у (#418/#425). */
  offerType: PlanOfferType;
  slug: string;
  displayName: string;
  shortDescription: string;
  longDescription: string;
  coverFileId: string | null;
  features: string[];
  priceCents: number | null;
  currency: string;
  /** Процент акционной скидки 1..99; null = акции нет. */
  discountPercent: number | null;
  discountStartsAt: string | null;
  discountEndsAt: string | null;
  /** Активна ли акция прямо сейчас (вычислено на сервере). */
  promotionActive: boolean;
  /** Цена с учётом активной акции; равна priceCents если акции нет. */
  effectivePriceCents: number | null;
  /**
   * Полный список курсов плана (bundle). Для COURSE-плана ≥1, для остальных
   * tier'ов пустой. Заменяет старый singular `courseId` (#404 — bundle plans).
   */
  courseIds: string[];
  includesFutureContent: boolean;
  trialDurationDays: number | null;
  capabilities: PlanCapability[];
  isHighlighted: boolean;
  termKind: string;
  termRecurringDays: number | null;
  isPublic: boolean;
  isActive: boolean;
  displayOrder: number;
  createdAt: string;
  archivedAt: string | null;
  /**
   * GitHub-org slug — юзер, состоящий в этом org, при логине через GitHub
   * автоматически получит plan-grant. null = привязки нет.
   */
  githubOrgSlug: string | null;
}

export interface PublicPlanCourseDto {
  id: string;
  title: string;
  /** Slug курса — для clickable-чипа курса в bundle-карточке (#404). */
  slug: string;
  /** Тип курса — для kind-бейджа в bundle-карточке (#404). */
  kind: CourseKind;
}

export interface PublicPlanDto {
  id: string;
  authorId: string;
  tier: PlanTier;
  /** Маркетинг-формат оффера — драйвер группировки/бейджа каталога (#418/#425). */
  offerType: PlanOfferType;
  slug: string;
  displayName: string;
  shortDescription: string;
  longDescription: string;
  coverFileId: string | null;
  features: string[];
  priceCents: number | null;
  currency: string;
  /** Процент акционной скидки 1..99; null = акции нет. */
  discountPercent: number | null;
  /** Конец окна акции (ISO, UTC); для хинта «до DD месяца». */
  discountEndsAt: string | null;
  /** Активна ли акция прямо сейчас (вычислено на сервере). */
  promotionActive: boolean;
  /** Цена с учётом активной акции; равна priceCents если акции нет. */
  effectivePriceCents: number | null;
  /**
   * Первый курс плана (legacy, для обратной совместимости). Для полного списка
   * bundle'а используй `courseIds` / `includedCourses` (#404).
   */
  courseId: string | null;
  /**
   * Полный список id курсов плана (bundle). Для COURSE-плана ≥1, для остальных
   * tier'ов пустой (#404).
   */
  courseIds: string[];
  includesFutureContent: boolean;
  trialDurationDays: number | null;
  capabilities: PlanCapability[];
  isHighlighted: boolean;
  /**
   * Срок действия плана. `LIFETIME` — разовая покупка, бессрочный grant;
   * `RECURRING` — подписка (#614), `termRecurringDays` несёт интервал автосписания
   * (напр. 30) — карточка рендерит «₽X / мес».
   */
  termKind: "LIFETIME" | "RECURRING";
  /** Интервал автопродления в днях для `RECURRING`; null для `LIFETIME`. */
  termRecurringDays: number | null;
  displayOrder: number;
  createdAt: string;
  /**
   * Курсы, входящие в план — populated backend'ом для COURSES kind через ECS
   * course-titles batch lookup (id + title + slug + kind). Null для других
   * kind'ов или если ECS lookup не сработал (graceful degrade).
   */
  includedCourses?: PublicPlanCourseDto[] | null;
}

/**
 * Lean Plan summary embedded into PlanGrantDto for the user-facing «мои планы»
 * view. Populated by GET /access/me/grants/.
 */
export interface PlanSummaryDto {
  id: string;
  authorId: string;
  slug: string;
  displayName: string;
  shortDescription: string | null;
  tier: PlanTier;
  coverFileId: string | null;
  capabilities: PlanCapability[];
  /** Курсы плана (bundle, #404). Backend `PlanSummaryDto.CourseIds`. */
  courseIds: string[];
  includesFutureContent: boolean;
  /** Длительность paid-trial. Null у обычных планов; undefined у старого API при rolling deploy. */
  trialDurationDays?: number | null;
  /**
   * У плана включён onboarding-flow. Frontend использует чтобы скрывать
   * «Пройти онбординг заново» у планов без настроенного flow.
   */
  hasOnboardingEnabled: boolean;
}

export interface PlanGrantDto {
  id: string;
  userId: string;
  planId: string;
  source: PlanGrantSource;
  sourceRef: string | null;
  grantedAt: string;
  expiresAt: string | null;
  status: PlanGrantStatus;
  revokedAt: string | null;
  revokeReason: string | null;
  /**
   * User enrichment — populated by ListPlanGrants (admin/author view) so the
   * GrantsTab renders email/name/avatar instead of raw UUID. Null on user-facing
   * endpoints (Redeem/AdminGrant create paths).
   */
  userEmail?: string | null;
  userDisplayName?: string | null;
  userUsername?: string | null;
  userAvatarId?: string | null;
  /**
   * Plan summary — populated by GetMyGrants (user view) so /settings/plans renders
   * plan cards without a second roundtrip per grant. Null on admin endpoints.
   */
  plan?: PlanSummaryDto | null;
  /** Следующее автосписание или retry. Null, если автопродление отключено. */
  nextChargeAt: string | null;
  /** Число подряд неудачных попыток списания в текущем dunning-цикле. */
  chargeFailureCount: number;
  /** Конец льготного доступа после неудачного списания. */
  renewalGraceEndsAt: string | null;
  /** Когда пользователь отключил будущие списания. */
  autoRenewalCancelledAt: string | null;
  /** Фактический конец доступа с учётом льготного периода. */
  accessEndsAt: string | null;
}

export interface InvitePreviewDto {
  planTier: PlanTier;
  planDisplayName: string;
  planShortDescription: string;
  planCoverFileId: string | null;
  planFeatures: string[];
  planCourseId: string | null;
  includesFutureContent: boolean;
  isAvailable: boolean;
  /** "invite.revoked" | "invite.expired" | "invite.usage.exhausted" | null */
  unavailableReason: string | null;
  planAuthorId: string;
}

// --- Author-side requests/dtos (Phase D) ---

export interface CreatePlanRequest {
  tier: PlanTier;
  slug: string;
  displayName: string;
  shortDescription?: string | null;
  longDescription?: string | null;
  coverFileId?: string | null;
  features?: string[] | null;
  priceCents?: number | null;
  currency?: string | null;
  /** Курсы плана (bundle). COURSE-tier ⇒ ≥1; остальные tier'ы — `[]`/опустить (#404). */
  courseIds?: string[] | null;
  displayOrder?: number | null;
  capabilities?: PlanCapability[] | null;
  isHighlighted?: boolean;
  /**
   * Маркетинг-формат оффера. null/опустить = дефолт по tier'у (FULL_ALL → FULL_ACCESS,
   * COURSE → COURSE). FULL_ACCESS на COURSE-tier отвергается бэкендом.
   */
  offerType?: string | null;
  /**
   * Срок временного доступа в днях (#580). Задаётся только при создании плана с
   * tier FULL_ALL — делает план «Полным доступом на месяц» (напр. 30): grant
   * получает `expiresAt`, после которого юзер доплачивает разницу до lifetime.
   * null/опустить = бессрочный план. Immutable после создания (нет в UpdatePlanRequest).
   *
   * @deprecated #595 — больше не передаётся фронтом из форм. Срок trial задаёт
   * сервер из конфига при `isTrial:true`. Поле оставлено для обратной совместимости.
   */
  trialDurationDays?: number | null;
  /**
   * #595 — создать «Пробный доступ» (singleton на платформу). Сервер форсит
   * tier FULL_ALL + FULL_ACCESS и сам ставит срок из конфига; дни НЕ передаём.
   * Повторная публикация → 409 `plan.trial.duplicate`.
   */
  isTrial?: boolean;
  /**
   * #614 — интервал автопродления подписки в днях. Обязателен и должен быть > 0
   * для `tier: "SUBSCRIPTION"` (домен отвергает иначе); игнорируется для прочих
   * тиров. Позволяет создать подписочный план «Тренажёр Pro» — backend сам
   * выставляет capability + offerType `TRAINER_PRO`.
   */
  recurringIntervalDays?: number | null;
}

export interface UpdatePlanRequest {
  displayName?: string;
  slug?: string;
  shortDescription?: string | null;
  longDescription?: string | null;
  coverFileId?: string | null;
  features?: string[] | null;
  priceCents?: number | null;
  currency?: string | null;
  /** Курсы плана (bundle). COURSE-tier ⇒ ≥1; остальные tier'ы — `[]`/опустить (#404). */
  courseIds?: string[] | null;
  displayOrder?: number | null;
  capabilities?: PlanCapability[] | null;
  isHighlighted?: boolean | null;
  /**
   * GitHub-org slug. Пустая строка <c>""</c> = снять привязку, null = не менять.
   * Backend нормализует к lowercase + проверяет format.
   */
  githubOrgSlug?: string | null;
  /**
   * Маркетинг-формат оффера. null = не менять. Tier-валидация в домене:
   * FULL_ALL/LEARN_ALL forced FULL_ACCESS; COURSE — COURSE/INTENSIVE/MARATHON.
   */
  offerType?: string | null;
}

/**
 * Установка акции на план — PUT /access/plans/{id}/promotion/.
 * Даты в ISO 8601 с offset'ом (UTC). endsAt позже startsAt и в будущем.
 */
export interface SetPromotionRequest {
  discountPercent: number;
  startsAt: string;
  endsAt: string;
}

export interface InviteLinkDto {
  id: string;
  planId: string;
  token: string;
  isActive: boolean;
  multiUse: boolean;
  maxUses: number | null;
  usageCount: number;
  expiresAt: string | null;
  label: string | null;
  createdAt: string;
}

export interface CreateInviteRequest {
  multiUse: boolean;
  maxUses?: number | null;
  expiresAt?: string | null;
  label?: string | null;
}

export interface AdminGrantRequest {
  planId: string;
  userId: string;
  expiresAt?: string | null;
}

/**
 * Lean user projection returned by GET /access/users/lookup — used by the
 * admin «Выдать grant» picker to find a user by displayName/username/Telegram.
 */
export interface UserLookupResultDto {
  userId: string;
  email: string;
  displayName: string | null;
  username: string | null;
  avatarId: string | null;
}

/**
 * Page response для author-side `GET /access/plans/{id}/grants/` с keyset pagination.
 */
export interface PlanGrantsPageDto {
  items: PlanGrantDto[];
  nextCursor: string | null;
}

/**
 * Phase 2 #112 — quote-расчёт upgrade-цены при покупке плана.
 * Возвращается `GET /access/plans/{planId}/upgrade-quote/`. Используется
 * pricing-страницей для показа индивидуальной цены залогиненному юзеру.
 *
 * - `originalPriceCents` — эффективная цена target-плана (с учётом активной акции).
 *   Совпадает с тем, что реально спишет Order. null если plan free / TBD.
 * - `creditCents` — sum(price_paid_cents) активных grants, scope ⊆ target. Cap'нут на original.
 * - `finalPriceCents` — `max(0, original - credit)`. null если original null.
 * - `isOwned` — true если у юзера уже есть ACTIVE grant на этот план.
 * - `sources` — breakdown «–25 000 ₽ за курс React» для UI.
 */
export interface UpgradeQuoteDto {
  originalPriceCents: number | null;
  creditCents: number;
  finalPriceCents: number | null;
  isOwned: boolean;
  sources: UpgradeCreditSourceDto[];
}

export interface UpgradeCreditSourceDto {
  grantId: string;
  planId: string;
  planDisplayName: string;
  planTier: string;
  creditCents: number;
}

// --- Access status / expired-access modal (#687) ---

/** Сводка состояния доступа для модалки «срок истёк». */
export interface AccessStatusDto {
  hasActiveAccess: boolean;
  recentlyExpired: ExpiredAccessDto | null;
}

/** Недавно истёкший доступ — питает текст и CTA модалки. */
export interface ExpiredAccessDto {
  grantId: string;
  planId: string;
  planName: string;
  tier: string;
  expiredAt: string;
}

// --- Per-plan stats dashboard (#289) ---

export interface PlanGrantTotalsDto {
  active: number;
  revoked: number;
  expired: number;
  total: number;
}

export interface PlanGrantPeriodCountersDto {
  last7Days: number;
  last30Days: number;
  last90Days: number;
}

export interface PlanGrantSourceBreakdownDto {
  source: PlanGrantSource;
  count: number;
}

/**
 * Один день в timeseries. `bySource` — точки на день по каждому источнику
 * (INVITE_LINK / AUTO_FREE / …) для stacked-chart. `count` — сумма по всем источникам.
 * Backend заполняет каждый день периода dense — пустые дни приходят с `count=0`.
 */
export interface PlanStatsTimeseriesPointDto {
  day: string; // ISO date "YYYY-MM-DD"
  count: number;
  bySource: Record<string, number>;
}

export interface PlanInviteLinkStatsDto {
  inviteLinkId: string;
  label: string | null;
  token: string;
  isActive: boolean;
  activationsCount: number;
  uniqueGrantsCount: number;
  firstActivationAt: string | null;
  lastActivationAt: string | null;
}

export interface PlanStatsDto {
  totals: PlanGrantTotalsDto;
  periodCounters: PlanGrantPeriodCountersDto;
  sourceBreakdown: PlanGrantSourceBreakdownDto[];
  timeseries: PlanStatsTimeseriesPointDto[];
  inviteLinks: PlanInviteLinkStatsDto[];
}

/**
 * Платформенный агрегат выручки по ВСЕМ планам с `offer_type=TRAINER_PRO` (#623) — для
 * админ-дашборда тренажёра. Кросс-плановый (не по одному плану). `mrrCentsEstimate` — оценка
 * MRR в копейках (сумма price_paid_cents активных). Зеркало `TrainerProRevenueDto`.
 */
export interface TrainerProRevenue {
  activeSubscriptions: number;
  activePayingCount: number;
  canceledSubscriptions: number;
  totalGrants: number;
  newInPeriod: PlanGrantPeriodCountersDto;
  mrrCentsEstimate: number;
  sourceBreakdown: PlanGrantSourceBreakdownDto[];
  timeseries: PlanStatsTimeseriesPointDto[];
}
