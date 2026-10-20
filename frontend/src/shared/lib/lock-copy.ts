import { routes } from "@/shared/config/routes";

/**
 * Unified lock-reason vocabulary for content access gating on the frontend.
 * Mirrors `ContentAccess.LockReasons` on the backend and is shared by
 * materials, collections, issues and course search —
 * all transport the same string literal from the API.
 */
export type LockReason =
  | "anonymous"
  // Legacy course-trial — backend LockReasonResolver всё ещё возвращает это
  // значение на course:X:trial тегах. Без ветки в resolveLockCopy CTA уходил
  // в неверный default.
  | "trial_required"
  | "not_enrolled"
  | "standard_required"
  | "plan_required";

export interface LockCopy {
  /** Bold headline for the callout card. */
  title: string;
  /** Short explanation shown under the title. */
  subtitle: string;
  /** CTA button label. */
  cta: string;
  /** Retained for existing callout consumers; sales closure provides no secondary action. */
  secondaryCta?: string;
  /** One-liner for tooltips / `aria-label` — no course title interpolation. */
  shortHint: string;
}

export function resolveLockCopy(
  reason: LockReason | null | undefined,
  courseTitle?: string | null,
): LockCopy {
  if (reason === "anonymous") {
    return {
      title: "Войдите, чтобы продолжить",
      subtitle: "Войдите, чтобы использовать ранее выданный доступ",
      cta: "Войти",
      shortHint: "Требуется вход",
    };
  }
  return {
    title: "Доступ ограничен",
    subtitle: courseTitle
      ? `Для курса «${courseTitle}» нужен ранее выданный доступ. Новые покупки закрыты.`
      : "Нужен ранее выданный доступ. Новые покупки закрыты.",
    cta: "Мои доступы",
    shortHint: "Нужен доступ к материалам",
  };
}

export interface UnlockHrefContext {
  lockReason: LockReason | null | undefined;
  /**
   * Текущий URL — для `anonymous` прокидываем в returnTo, чтобы после логина
   * пользователь вернулся туда, где нажал на замок.
   */
  returnTo?: string | null;
}

/**
 * Единый резолвер unlock-URL: куда вести пользователя при клике по замку.
 * `anonymous` → /login, остальное — в обзор существующих доступов.
 * Используется MaterialCard, CollectionCard, SearchItem, CurriculumItem и
 * любыми новыми locked-карточками. Зачисление на курсы — только через план,
 * никаких Telegram-консультаций в lock-flow.
 */
export function resolveUnlockHref({ lockReason, returnTo }: UnlockHrefContext): string | null {
  if (!lockReason) return null;
  if (lockReason === "anonymous") {
    if (returnTo) {
      const qs = new URLSearchParams({ callbackUrl: returnTo }).toString();
      return `/login?${qs}`;
    }
    return "/login";
  }
  return routes.myPlans;
}

/** Sales are closed; locked content has no purchase action. */
export function resolveSecondaryUnlockHref(_context: UnlockHrefContext): string | null {
  return null;
}
