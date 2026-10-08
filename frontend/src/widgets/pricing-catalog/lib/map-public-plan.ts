import { formatPromotionEndsHint, type PublicPlanDto } from "@/entities/access-plan";
import type { PlanCard, PlanCardKind } from "@/shared/config/landing-plans";

const RUB_FORMATTER = new Intl.NumberFormat("ru-RU");

function rubLabel(rubles: number, currency: string): string {
  return `${RUB_FORMATTER.format(rubles)} ${currency === "RUB" ? "₽" : currency}`;
}

/**
 * Маппит backend `PublicPlanDto` в shape, который ожидает существующий
 * PricingCatalog UI (см. shared/config/landing-plans.ts).
 *
 * Issue #358: FREE-kind план больше не предлагается пользователю как отдельный
 * offering — бесплатный доступ = system default (REGISTERED). Backend-fetch
 * планов фильтрует архивные FREE-планы по `is_active=false`, поэтому до этого
 * маппера они не дойдут. Defensive-ветка ниже маппит их как «tbd» на случай
 * неконсистентного состояния.
 *
 * Две ветки:
 * - **Платный с ценой** (`priceCents > 0`) → priceLabel в рублях, CTA
 *   «Оплатить XXX ₽», installment если ≥ 30k.
 * - **Платный без цены** (`priceCents=null|0`) → priceLabel «По запросу»,
 *   CTA «Узнать цену» с redirect в Telegram (заглушка пока не подключён
 *   payment provider). Это типичный case когда автор только создал план
 *   и ещё не выставил цену через UI.
 */
export function mapPublicPlanToCard(dto: PublicPlanDto): PlanCard {
  const hasPrice = dto.priceCents != null && dto.priceCents > 0;
  const listRubles = hasPrice ? Math.floor(dto.priceCents! / 100) : 0;

  // Акция: при активном промо используем эффективную (сниженную) цену как
  // основную — и для лейбла, и для checkout'а (priceCents), и для рассрочки.
  // Старую цену прокидываем отдельно (зачёркнутая) + процент + хинт окончания.
  const promoActive = hasPrice && dto.promotionActive && dto.effectivePriceCents != null;
  const effectiveCents = promoActive ? dto.effectivePriceCents! : hasPrice ? dto.priceCents! : 0;
  const priceRubles = Math.floor(effectiveCents / 100);

  let cardKind: PlanCardKind;
  let priceLabel: string;
  let paymentCta: string;
  let cta: string;

  if (hasPrice) {
    cardKind = "paid";
    priceLabel = rubLabel(priceRubles, dto.currency);
    paymentCta = `Оплатить ${priceLabel}`;
    cta = "Получить доступ";
  } else {
    // Платный план без выставленной цены или legacy FREE — заглушка через Telegram.
    cardKind = "tbd";
    priceLabel = "По запросу";
    paymentCta = "Узнать цену";
    cta = "Узнать цену";
  }

  const badge = badgeFromTier(dto.tier, dto.isHighlighted);
  const accent: PlanCard["accent"] = dto.isHighlighted ? "gold" : "neutral";

  const features = (dto.features ?? []).map((text) => ({ text }));
  const comparisonKeys = (dto.capabilities ?? []).map((cap) => `cap-${cap}`);

  return {
    id: dto.slug,
    planId: dto.id,
    badge,
    name: dto.displayName,
    description: dto.shortDescription || "",
    longDescription: dto.longDescription || dto.shortDescription || "",
    price: priceLabel,
    priceAmount: priceRubles,
    priceCents: hasPrice ? effectiveCents : undefined,
    currency: dto.currency,
    originalPriceLabel: promoActive ? rubLabel(listRubles, dto.currency) : null,
    discountPercent: promoActive ? dto.discountPercent : null,
    discountEndsHint: promoActive ? formatPromotionEndsHint(dto.discountEndsAt) : null,
    priceNote: priceNoteFromTier(dto.tier, cardKind, dto.termRecurringDays),
    installmentLabel:
      cardKind === "paid" && priceRubles >= 30000
        ? `4 платежа по ${RUB_FORMATTER.format(Math.floor(priceRubles / 4))} ₽`
        : null,
    features,
    comparisonKeys,
    cta,
    ctaHref: `#${dto.slug}`,
    paymentCta,
    accent,
    highlighted: dto.isHighlighted,
    kind: cardKind,
    tier: dto.tier,
    offerType: dto.offerType,
    termRecurringDays: dto.termRecurringDays,
    courseId: dto.courseId,
    courseIds: dto.courseIds ?? [],
    includedCourses:
      dto.includedCourses?.map((c) => ({
        id: c.id,
        title: c.title,
        slug: c.slug,
        kind: c.kind,
      })) ?? null,
    trialDurationDays: dto.trialDurationDays,
  };
}

function badgeFromTier(kind: PublicPlanDto["tier"], isHighlighted: boolean): string {
  if (isHighlighted) return "Самое полное · Хит";
  switch (kind) {
    case "FULL_ALL":
      return "Полный доступ";
    case "LEARN_ALL":
      return "Все материалы";
    case "COURSE":
      return "Только материалы";
    case "SUBSCRIPTION":
      return "Подписка";
    case "FREE":
      // Legacy archived FREE-плана дошёл до UI — defensive label.
      return "Архивный";
    default:
      return "План";
  }
}

function priceNoteFromTier(
  kind: PublicPlanDto["tier"],
  cardKind: PlanCardKind,
  termRecurringDays?: number | null,
): string {
  if (cardKind === "tbd") return "уточняется автором";
  switch (kind) {
    case "FULL_ALL":
    case "COURSE":
      return "разовая оплата · доступ навсегда";
    case "SUBSCRIPTION":
      // Помесячная подписка ≈30 дней → «в месяц · автопродление»; иной интервал —
      // «каждые N дней». Лейбл «₽X / мес» рендерит сама карточка из cadenceLabel.
      return termRecurringDays != null && termRecurringDays > 0 && termRecurringDays <= 31
        ? "в месяц · автопродление"
        : termRecurringDays != null && termRecurringDays > 0
          ? `каждые ${termRecurringDays} дн. · автопродление`
          : "подписка · автопродление";
    default:
      return "";
  }
}
