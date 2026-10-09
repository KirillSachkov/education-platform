import type { PlanGrantDto, PublicPlanDto } from "./types";

/**
 * Trial-план (#580) — платный «Пробный месяц»: даёт полный доступ на
 * `trialDurationDays` дней, после чего юзер доплачивает разницу до lifetime.
 * Детектится единственным признаком: ненулевой `trialDurationDays`.
 */
export function isTrialPlan(plan: PublicPlanDto): boolean {
  return (plan.trialDurationDays ?? 0) > 0;
}

/**
 * Активный paid-trial grant (#580). `expiresAt` задаёт срок конкретного гранта,
 * а `trialDurationDays` отличает upgrade-план от других временных доступов.
 * Питает баннер «доплати до полного» в «Моих планах».
 */
export function isActiveTrialGrant(grant: PlanGrantDto): boolean {
  const trialDurationDays = grant.plan?.trialDurationDays;
  // Старый AccessService не присылает поле во время rolling deploy. До его
  // обновления сохраняем прежний expiresAt-based fallback.
  return (
    grant.status === "ACTIVE" &&
    grant.expiresAt != null &&
    grant.plan != null &&
    (trialDurationDays === undefined || (trialDurationDays ?? 0) > 0)
  );
}

/** У юзера уже есть полный доступ к обучению .NET Fullstack — активный грант на FULL_ALL/LEARN_ALL план или план с includesFutureContent (#580). */
export function hasFullAccessGrant(grants: PlanGrantDto[]): boolean {
  return grants.some(
    (g) =>
      g.status === "ACTIVE" &&
      g.plan != null &&
      (g.plan.tier === "FULL_ALL" || g.plan.tier === "LEARN_ALL" || g.plan.includesFutureContent),
  );
}

/**
 * Покрывает ли план данный курс. План покрывает курс если:
 * - это `FULL_ALL` (весь контент платформы), ИЛИ
 * - курс есть в `includedCourses` (backend-список, точный), ИЛИ
 * - курс есть в `courseIds` (полный bundle-набор), ИЛИ
 * - legacy single `courseId` совпадает.
 *
 * Живёт в entities, чтобы и feature'ы (course-purchase-options, course-catalog),
 * и виджеты могли фильтровать планы вниз по слою без cross-slice импорта.
 */
export function planCoversCourse(plan: PublicPlanDto, courseId: string): boolean {
  if (plan.tier === "FULL_ALL") return true;
  if (plan.includedCourses?.some((c) => c.id === courseId)) return true;
  if (plan.courseIds?.includes(courseId)) return true;
  return plan.courseId === courseId;
}

const RUB = new Intl.NumberFormat("ru-RU");
const PROMO_DATE = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long" });

/**
 * Форматирует цену из копеек в рубли: 840000 → «8 400 ₽».
 * Для валют кроме RUB подставляет код валюты вместо символа.
 */
export function formatPriceFromCents(cents: number, currency = "RUB"): string {
  const whole = Math.floor(cents / 100);
  return `${RUB.format(whole)} ${currency === "RUB" ? "₽" : currency}`;
}

/**
 * Акционная цена в копейках по проценту скидки. Зеркалит целочисленную
 * арифметику бэкенда (`Plan.EffectivePriceCents`) — truncate, не округление.
 */
export function discountedCents(priceCents: number, discountPercent: number): number {
  return Math.max(0, priceCents - Math.floor((priceCents * discountPercent) / 100));
}

/**
 * Хинт окончания акции для витрины: «до 12 июня». null если даты нет/невалидна.
 */
export function formatPromotionEndsHint(endsAtIso: string | null | undefined): string | null {
  if (!endsAtIso) return null;
  const date = new Date(endsAtIso);
  if (Number.isNaN(date.getTime())) return null;
  return `до ${PROMO_DATE.format(date)}`;
}

/**
 * Каноничный порядок отображения публичных планов в каталоге и витринах:
 *   1. Highlighted-план (`isHighlighted=true`) — flagship оффер платформы, всегда первым
 *   2. По `displayOrder` (asc) — автор/owner задаёт вручную
 *   3. По `createdAt` (asc) — стабильный tie-breaker
 *
 * Используется в `/pricing` carousel и в `PlansShowcase` на лендинге.
 */
export function sortPublicPlans(plans: PublicPlanDto[]): PublicPlanDto[] {
  return [...plans].sort((a, b) => {
    if (a.isHighlighted !== b.isHighlighted) {
      return a.isHighlighted ? -1 : 1;
    }
    if (a.displayOrder !== b.displayOrder) {
      return a.displayOrder - b.displayOrder;
    }
    return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
  });
}
