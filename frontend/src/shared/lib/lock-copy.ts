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
  /**
   * Optional secondary CTA — currently used only for `anonymous` to offer
   * "Войти" (primary) + "Посмотреть планы" (secondary). Other reasons rely on
   * a single CTA because the unlock action has no fork.
   */
  secondaryCta?: string;
  /** One-liner for tooltips / `aria-label` — no course title interpolation. */
  shortHint: string;
}

export function resolveLockCopy(
  reason: LockReason | null | undefined,
  courseTitle?: string | null,
): LockCopy {
  switch (reason) {
    case "anonymous":
      return {
        title: "Войдите, чтобы открыть",
        subtitle: "Контент доступен после авторизации или выбора плана",
        cta: "Войти",
        secondaryCta: "Посмотреть планы",
        shortHint: "Войдите, чтобы открыть",
      };
    case "trial_required":
      return {
        title: "Доступно по плану",
        subtitle: courseTitle
          ? `Откройте курс «${courseTitle}» по подходящему плану`
          : "Откройте курс по подходящему плану",
        cta: "Выбрать план",
        shortHint: "Нужен план доступа к курсу",
      };
    case "standard_required":
      return {
        title: "Доступно после полной записи",
        subtitle: courseTitle
          ? `Откройте курс «${courseTitle}» по подходящему плану`
          : "Откройте курс по подходящему плану",
        cta: "Выбрать план",
        shortHint: "Нужна полная запись на курс",
      };
    case "plan_required":
      return {
        title: "Нужен план доступа",
        subtitle: courseTitle
          ? `Откроется по плану, покрывающему курс «${courseTitle}»`
          : "Откроется по подходящему плану доступа",
        cta: "Выбрать план",
        shortHint: "Нужен план доступа",
      };
    case "not_enrolled":
    default:
      return {
        title: "Доступно ученикам курса",
        subtitle: courseTitle
          ? `Откройте курс «${courseTitle}» по подходящему плану`
          : "Откройте курс по подходящему плану",
        cta: "Выбрать план",
        shortHint: "Нужен план доступа к курсу",
      };
  }
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
 * `anonymous` → /login, остальное — в глобальный каталог планов (`/pricing`).
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
  return routes.pricing;
}

/**
 * Secondary unlock URL — нужен только для `anonymous`, чтобы предложить два
 * пути: "Войти" (primary, `resolveUnlockHref`) и "Посмотреть планы" (secondary,
 * этот резолвер). Для остальных reasons возвращает `null`.
 */
export function resolveSecondaryUnlockHref({ lockReason }: UnlockHrefContext): string | null {
  if (lockReason !== "anonymous") return null;
  return routes.pricing;
}
