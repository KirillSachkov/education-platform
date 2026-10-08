/**
 * Маркетинг-формат оффера плана (см. `PlanOfferType` на бэке, issue #418/#425).
 * Регистр совпадает с C# enum members — храним и шлём строго в UPPER_SNAKE_CASE.
 *
 * Ортогонален `PlanTier` (что открывает доступ): offer-type определяет, КАК план
 * презентуется в каталоге/landing'е — бейдж, копирайт, секция. Связь с tier'ом
 * форсится бэкендом: FULL_ALL/LEARN_ALL → FULL_ACCESS; COURSE → один из
 * COURSE/INTENSIVE/MARATHON; SUBSCRIPTION → TRAINER_PRO.
 *
 * - `FULL_ACCESS` — «полный доступ» (flagship-оффер платформы).
 * - `COURSE` — оффер на конкретный курс или bundle курсов.
 * - `INTENSIVE` — оффер-интенсив (короткий формат без заданий).
 * - `MARATHON` — оффер-марафон (групповой формат без заданий).
 * - `TRAINER_PRO` — подписка на тренажёр собеседований (Trainer Pro, #614). Форсится
 *   бэкендом для `PlanTier.SUBSCRIPTION`; рендерится отдельной секцией на pricing.
 */
export const OFFER_TYPES = ["FULL_ACCESS", "COURSE", "INTENSIVE", "MARATHON", "TRAINER_PRO"] as const;

export type PlanOfferType = (typeof OFFER_TYPES)[number];

export const OFFER_TYPE_LABELS: Record<PlanOfferType, string> = {
  FULL_ACCESS: "Полный доступ",
  COURSE: "Курс",
  INTENSIVE: "Интенсив",
  MARATHON: "Марафон",
  TRAINER_PRO: "Тренажёр",
};

/**
 * Единый источник правды для визуального бейджа offer-type плана в каталоге.
 * Цвет — НИКОГДА не единственный сигнал: всегда в паре с текстовым `badgeLabel`.
 *
 * Палитра согласована с `COURSE_KIND_VISUALS` (приглушённый тёмно-стеклянный стиль)
 * — INTENSIVE/MARATHON переиспользуют те же teal/cyan классы, чтобы offer-бейджи
 * и course-kind-бейджи не расходились. FULL_ACCESS — золото (флагман, премиум-акцент).
 * TRAINER_PRO — фиолетовый (отдельный премиум-add-on, не путается с full-access золотом).
 * COURSE — нейтральный (`null` бейдж: обычный платный курс не маркируется).
 */
export const OFFER_TYPE_VISUALS: Record<
  PlanOfferType,
  { badgeLabel: string; badgeClass: string } | null
> = {
  FULL_ACCESS: {
    badgeLabel: OFFER_TYPE_LABELS.FULL_ACCESS,
    badgeClass: "bg-amber-950/55 text-amber-200/90 backdrop-blur-sm",
  },
  COURSE: null,
  INTENSIVE: {
    badgeLabel: OFFER_TYPE_LABELS.INTENSIVE,
    badgeClass: "bg-teal-950/55 text-teal-200/90 backdrop-blur-sm",
  },
  MARATHON: {
    badgeLabel: OFFER_TYPE_LABELS.MARATHON,
    badgeClass: "bg-cyan-950/55 text-cyan-200/90 backdrop-blur-sm",
  },
  TRAINER_PRO: {
    badgeLabel: OFFER_TYPE_LABELS.TRAINER_PRO,
    badgeClass: "bg-violet-950/55 text-violet-200/90 backdrop-blur-sm",
  },
};

/**
 * Хелпер для каталожного бейджа offer-type: `{ label, class }` либо `null`
 * (для COURSE — обычный платный курс без отдельного бейджа).
 */
export function getOfferTypeBadge(offerType: PlanOfferType): { label: string; class: string } | null {
  const visual = OFFER_TYPE_VISUALS[offerType];
  return visual ? { label: visual.badgeLabel, class: visual.badgeClass } : null;
}
