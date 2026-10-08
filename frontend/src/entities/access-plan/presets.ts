import type { PlanCapability, PlanTier } from "./types";

/**
 * Plan UI-presets — упорядоченный список tier'ов для radio в Create-форме.
 * Phase 1.3 #111: tier — single source-of-truth, capabilities выводятся из tier'а
 * (для LEARN_ALL/FULL_ALL фиксированы; для COURSE автор настраивает).
 *
 * Issue #358: FREE preset удалён — автор больше не создаёт FREE-планы вручную;
 * бесплатный доступ = system default (REGISTERED) на уровне AccessType.
 *
 * LEARN_ALL preset удалён — «доступ только к материалам» как отдельный план
 * упразднён. Остаются: общий план (FULL_ALL) + планы на конкретные курсы (COURSE).
 * Backend тоже запрещает создание LEARN_ALL (plan.learn_all.deprecated); enum-значение
 * сохранено только для legacy/архивных строк (как FREE).
 */
export type PlanPreset = "FULL_ALL" | "COURSE";

const FULL_CAPABILITIES = new Set<PlanCapability>([
  "VIEW_MATERIALS",
  "SUBMIT_ISSUES",
  "CODE_REVIEW",
  "COMMUNITY_ACCESS",
  "LIVE_CALLS",
  "JOB_SUPPORT",
]);

export interface PlanPresetSpec {
  preset: PlanPreset;
  label: string;
  hint: string;
  tier: PlanTier;
  /** null = capabilities на выбор автора (только для COURSE). */
  capabilities: ReadonlySet<PlanCapability> | null;
  /** Singleton: один такой опубликованный план на платформу (LEARN_ALL/FULL_ALL). */
  isSingleton: boolean;
}

export const PLAN_PRESETS: ReadonlyArray<PlanPresetSpec> = [
  {
    preset: "FULL_ALL",
    label: "Полный доступ .NET Fullstack",
    hint: "Все курсы направления .NET Fullstack (включая будущие) + полный набор возможностей: код-ревью, чат, созвоны.",
    tier: "FULL_ALL",
    capabilities: FULL_CAPABILITIES,
    isSingleton: true,
  },
  {
    preset: "COURSE",
    label: "Подборка курсов",
    hint: "Доступ только к выбранному списку курсов. Возможности — настроите сами ниже.",
    tier: "COURSE",
    capabilities: null,
    isSingleton: false,
  },
];

/**
 * Восстанавливает preset по tier плана. Для legacy `FREE`-планов
 * (архивные после #358) возвращает null — пресета для отображения нет.
 */
export function detectPreset(tier: PlanTier): PlanPreset | null {
  const found = PLAN_PRESETS.find((p) => p.tier === tier);
  return found?.preset ?? null;
}
