import { describe, expect, it } from "vitest";
import type { UpgradeQuoteDto } from "@/entities/access-plan";
import { personalPriceParts } from "../personal-price";

const BASE = { priceLabel: "95 200 ₽", currency: "RUB" };

function buildQuote(overrides: Partial<UpgradeQuoteDto> = {}): UpgradeQuoteDto {
  return {
    originalPriceCents: 9_520_000,
    creditCents: 792_000,
    finalPriceCents: 8_728_000,
    isOwned: false,
    sources: [],
    ...overrides,
  };
}

describe("personalPriceParts", () => {
  it("quote с кредитом → главная цена = финальная, прежняя — зачёркнутая", () => {
    const parts = personalPriceParts(BASE, buildQuote());

    expect(parts.hasCredit).toBe(true);
    expect(parts.priceLabel).toBe(`${(87280).toLocaleString("ru-RU")} ₽`);
    expect(parts.strikeLabel).toBe("95 200 ₽");
  });

  it("без quote (аноним / static-план) → базовая цена без зачёркивания", () => {
    const parts = personalPriceParts(BASE, undefined);

    expect(parts.hasCredit).toBe(false);
    expect(parts.priceLabel).toBe("95 200 ₽");
    expect(parts.strikeLabel).toBeNull();
  });

  it("creditCents = 0 → базовая цена", () => {
    const parts = personalPriceParts(BASE, buildQuote({ creditCents: 0 }));

    expect(parts.hasCredit).toBe(false);
    expect(parts.priceLabel).toBe("95 200 ₽");
  });

  it("isOwned → базовая цена (карточка показывает «уже есть план»)", () => {
    const parts = personalPriceParts(BASE, buildQuote({ isOwned: true }));

    expect(parts.hasCredit).toBe(false);
    expect(parts.strikeLabel).toBeNull();
  });

  it("finalPriceCents = null (план без цены) → базовая цена", () => {
    const parts = personalPriceParts(BASE, buildQuote({ finalPriceCents: null }));

    expect(parts.hasCredit).toBe(false);
  });

  it("полный кредит (final = 0) → «0 ₽» как главная цена", () => {
    const parts = personalPriceParts(
      BASE,
      buildQuote({ creditCents: 9_520_000, finalPriceCents: 0 }),
    );

    expect(parts.hasCredit).toBe(true);
    expect(parts.priceLabel).toBe("0 ₽");
  });

  it("не-RUB валюта → код валюты вместо символа", () => {
    const parts = personalPriceParts(
      { priceLabel: "1 000 USD", currency: "USD" },
      buildQuote({ finalPriceCents: 50_000 }),
    );

    expect(parts.priceLabel).toBe("500 USD");
  });
});
