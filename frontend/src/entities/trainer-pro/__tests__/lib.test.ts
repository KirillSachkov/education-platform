import { describe, expect, it } from "vitest";
import { isMonthlyTrainerInterval, resolveTrainerProOfferCard } from "../lib";
import type { TrainerProOfferDto } from "../types";

function offer(overrides: Partial<TrainerProOfferDto> = {}): TrainerProOfferDto {
  return {
    id: "plan-1",
    slug: "trainer-pro",
    displayName: "Тренажёр Pro",
    shortDescription: "",
    longDescription: "",
    coverFileId: null,
    features: [],
    priceCents: 99000,
    currency: "RUB",
    discountPercent: null,
    discountEndsAt: null,
    promotionActive: false,
    effectivePriceCents: 99000,
    recurringIntervalDays: 30,
    capabilities: ["TRAINER_PRO"],
    isHighlighted: false,
    displayOrder: 0,
    ...overrides,
  };
}

describe("resolveTrainerProOfferCard", () => {
  it("null для undefined / пустого списка", () => {
    expect(resolveTrainerProOfferCard(undefined)).toBeNull();
    expect(resolveTrainerProOfferCard([])).toBeNull();
  });

  it("берёт эффективную цену (акция) приоритетнее базовой", () => {
    const card = resolveTrainerProOfferCard([
      offer({ priceCents: 100000, effectivePriceCents: 70000 }),
    ]);
    expect(card?.priceCents).toBe(70000);
  });

  it("фолбэк на priceCents когда effectivePriceCents == null", () => {
    const card = resolveTrainerProOfferCard([offer({ effectivePriceCents: null, priceCents: 50000 })]);
    expect(card?.priceCents).toBe(50000);
  });

  it("null когда у выбранного оффера нет положительной цены", () => {
    expect(resolveTrainerProOfferCard([offer({ effectivePriceCents: 0, priceCents: 0 })])).toBeNull();
    expect(
      resolveTrainerProOfferCard([offer({ effectivePriceCents: null, priceCents: null })]),
    ).toBeNull();
  });

  it("highlighted-оффер приоритетнее первого в списке", () => {
    const card = resolveTrainerProOfferCard([
      offer({ id: "a", isHighlighted: false }),
      offer({ id: "b", isHighlighted: true, priceCents: 12300, effectivePriceCents: 12300 }),
    ]);
    expect(card?.planId).toBe("b");
  });

  it("без highlighted берёт первый (бэкенд уже сортирует по displayOrder)", () => {
    const card = resolveTrainerProOfferCard([offer({ id: "a" }), offer({ id: "b" })]);
    expect(card?.planId).toBe("a");
  });

  it("monthly=true для интервала ≤31, features прокидываются как есть", () => {
    const card = resolveTrainerProOfferCard([
      offer({ recurringIntervalDays: 30, features: ["Голос", "Мок"] }),
    ]);
    expect(card?.monthly).toBe(true);
    expect(card?.features).toEqual(["Голос", "Мок"]);
    expect(card?.currency).toBe("RUB");
  });

  it("monthly=false для длинного интервала / null", () => {
    expect(resolveTrainerProOfferCard([offer({ recurringIntervalDays: 365 })])?.monthly).toBe(false);
    expect(resolveTrainerProOfferCard([offer({ recurringIntervalDays: null })])?.monthly).toBe(false);
  });
});

describe("isMonthlyTrainerInterval", () => {
  it("граница окна 1..31", () => {
    expect(isMonthlyTrainerInterval(1)).toBe(true);
    expect(isMonthlyTrainerInterval(31)).toBe(true);
    expect(isMonthlyTrainerInterval(32)).toBe(false);
    expect(isMonthlyTrainerInterval(0)).toBe(false);
    expect(isMonthlyTrainerInterval(null)).toBe(false);
    expect(isMonthlyTrainerInterval(undefined)).toBe(false);
  });
});
