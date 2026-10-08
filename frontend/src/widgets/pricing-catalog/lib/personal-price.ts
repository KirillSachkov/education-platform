import type { UpgradeQuoteDto } from "@/entities/access-plan";

export interface PersonalPriceParts {
  /** Крупная цена карточки: личная итоговая при upgrade-credit, иначе базовая. */
  priceLabel: string;
  /** Зачёркнутая цена до вычета кредита. null — кредита нет, действует обычный promo-ряд. */
  strikeLabel: string | null;
  hasCredit: boolean;
}

/**
 * #521: личная цена как ГЛАВНАЯ цена карточки. Если upgrade-quote (#486) дал
 * credit — крупно показываем finalPriceCents, а прежнюю (эффективную) цену —
 * зачёркнутой рядом. Promo-ряд «112 000 ₽ −15%» при этом скрывается: его процент
 * считается от list-цены и рядом с финальной суммой врал бы.
 */
export function personalPriceParts(
  base: { priceLabel: string; currency: string },
  quote: UpgradeQuoteDto | null | undefined,
): PersonalPriceParts {
  if (!quote || quote.isOwned || quote.creditCents <= 0 || quote.finalPriceCents == null) {
    return { priceLabel: base.priceLabel, strikeLabel: null, hasCredit: false };
  }

  const rubles = Math.floor(quote.finalPriceCents / 100);
  const symbol = base.currency === "RUB" ? "₽" : base.currency;
  return {
    priceLabel: `${rubles.toLocaleString("ru-RU")} ${symbol}`,
    strikeLabel: base.priceLabel,
    hasCredit: true,
  };
}
