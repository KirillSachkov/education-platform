import type { TrainerProOfferDto } from "./types";

/** Производный «оффер-карточка» Trainer Pro для лендинга/пейволла/CTA. */
export interface TrainerProOfferCard {
  planId: string;
  /** Эффективная цена (с учётом акции), в копейках. Всегда > 0. */
  priceCents: number;
  currency: string;
  /** Помесячная подписка (для лейбла «/мес»). */
  monthly: boolean;
  /** Список преимуществ оффера (может быть пустым). */
  features: string[];
}

/** Помесячный интервал автосписания (≈30 дней) — для лейбла «/мес». */
export function isMonthlyTrainerInterval(days: number | null | undefined): boolean {
  return days != null && days > 0 && days <= 31;
}

/**
 * Выбирает покупаемый оффер Trainer Pro из публичного списка (`GET /access/trainer-pro/offer`):
 * highlighted-first, иначе первый (бэкенд уже сортирует по displayOrder/createdAt). Цена —
 * эффективная (с учётом акции), как на pricing. `null`, если офферов нет ИЛИ у выбранного нет
 * положительной цены — тогда покупать нечего, UI показывает «Подписка скоро появится».
 */
export function resolveTrainerProOfferCard(
  offers: TrainerProOfferDto[] | undefined,
): TrainerProOfferCard | null {
  if (!offers || offers.length === 0) return null;
  const offer = offers.find((o) => o.isHighlighted) ?? offers[0];

  const priceCents = offer.effectivePriceCents ?? offer.priceCents ?? null;
  if (priceCents == null || priceCents <= 0) return null;

  return {
    planId: offer.id,
    priceCents,
    currency: offer.currency,
    monthly: isMonthlyTrainerInterval(offer.recurringIntervalDays),
    features: offer.features ?? [],
  };
}
