import { describe, expect, it } from "vitest";
import type { PublicPlanDto } from "@/entities/access-plan";
import { mapPublicPlanToCard } from "../map-public-plan";

function buildDto(overrides: Partial<PublicPlanDto> = {}): PublicPlanDto {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    authorId: "00000000-0000-0000-0000-000000000000",
    tier: "FULL_ALL",
    offerType: "FULL_ACCESS",
    slug: "test",
    displayName: "Test plan",
    shortDescription: "Short",
    longDescription: "Long",
    coverFileId: null,
    features: ["Feature A", "Feature B"],
    priceCents: 5000000,
    currency: "RUB",
    discountPercent: null,
    discountEndsAt: null,
    promotionActive: false,
    effectivePriceCents: null,
    courseId: null,
    courseIds: [],
    includesFutureContent: true,
    trialDurationDays: null,
    capabilities: ["VIEW_MATERIALS", "SUBMIT_ISSUES"],
    isHighlighted: false,
    termKind: "LIFETIME",
    termRecurringDays: null,
    displayOrder: 0,
    createdAt: "2026-01-01T00:00:00Z",
    ...overrides,
  };
}

describe("mapPublicPlanToCard", () => {
  it("маппит платный LIFETIME_ALL план — рубли + бейдж + рассрочка", () => {
    const card = mapPublicPlanToCard(buildDto({ priceCents: 5000000 })); // 50000 ₽

    expect(card.id).toBe("test");
    expect(card.name).toBe("Test plan");
    expect(card.price).toContain("50");
    expect(card.priceAmount).toBe(50000);
    expect(card.badge).toBe("Полный доступ");
    expect(card.priceNote).toBe("разовая оплата · доступ навсегда");
    expect(card.installmentLabel).toBeTruthy();
    expect(card.paymentCta).toContain("Оплатить");
    expect(card.kind).toBe("paid");
  });

  it("платный план без priceCents → tbd, CTA «Узнать цену»", () => {
    const card = mapPublicPlanToCard(buildDto({
      tier: "FULL_ALL",
      priceCents: null,
    }));

    expect(card.kind).toBe("tbd");
    expect(card.price).toBe("По запросу");
    expect(card.paymentCta).toBe("Узнать цену");
    expect(card.priceNote).toBe("уточняется автором");
    expect(card.installmentLabel).toBeNull();
  });

  it("highlighted план получает gold-accent + специальный бейдж", () => {
    const card = mapPublicPlanToCard(buildDto({ isHighlighted: true }));
    expect(card.highlighted).toBe(true);
    expect(card.accent).toBe("gold");
    expect(card.badge).toBe("Самое полное · Хит");
  });

  it("дешёвый платный план (<30k) — без рассрочки", () => {
    const card = mapPublicPlanToCard(buildDto({ priceCents: 1000000 })); // 10000
    expect(card.installmentLabel).toBeNull();
  });

  it("features маппятся в PlanFeature[]", () => {
    const card = mapPublicPlanToCard(buildDto({ features: ["A", "B", "C"] }));
    expect(card.features).toHaveLength(3);
    expect(card.features[0]).toEqual({ text: "A" });
  });

  it("comparisonKeys derived из capabilities", () => {
    const card = mapPublicPlanToCard(buildDto({
      capabilities: ["VIEW_MATERIALS", "CODE_REVIEW"],
    }));
    expect(card.comparisonKeys).toEqual(["cap-VIEW_MATERIALS", "cap-CODE_REVIEW"]);
  });

  it("COURSES kind → бейдж «Только материалы»", () => {
    const card = mapPublicPlanToCard(buildDto({ tier: "COURSE", priceCents: 1500000 }));
    expect(card.badge).toBe("Только материалы");
  });

  it("legacy archived FREE-плана (defensive) → бейдж «Архивный», kind=tbd", () => {
    // Issue #358: FREE-плана не должны доходить до UI (фильтруются is_active=false),
    // но если дошёл — рендерится без CTA «Получить бесплатно».
    const card = mapPublicPlanToCard(buildDto({ tier: "FREE", priceCents: null }));
    expect(card.badge).toBe("Архивный");
    expect(card.kind).toBe("tbd");
  });

  it("активная акция — эффективная цена в лейбле/checkout + старая цена зачёркнута + процент", () => {
    const card = mapPublicPlanToCard(
      buildDto({
        priceCents: 5000000, // 50 000 ₽
        promotionActive: true,
        discountPercent: 30,
        effectivePriceCents: 3500000, // 35 000 ₽
        discountEndsAt: "2099-06-12T00:00:00Z",
      }),
    );

    expect(card.price).toContain("35"); // эффективная
    expect(card.priceCents).toBe(3500000); // checkout charges discounted
    expect(card.priceAmount).toBe(35000);
    expect(card.originalPriceLabel).toContain("50"); // зачёркнутая старая
    expect(card.discountPercent).toBe(30);
    expect(card.discountEndsHint).toBeTruthy();
  });

  it("trial-план (#580) — trialDurationDays прокидывается в карточку", () => {
    const card = mapPublicPlanToCard(buildDto({ trialDurationDays: 30 }));
    expect(card.trialDurationDays).toBe(30);
  });

  it("обычный план — trialDurationDays = null", () => {
    const card = mapPublicPlanToCard(buildDto({ trialDurationDays: null }));
    expect(card.trialDurationDays).toBeNull();
  });

  it("неактивная акция (вне окна) — показывается обычная цена, без бейджа", () => {
    const card = mapPublicPlanToCard(
      buildDto({
        priceCents: 5000000,
        promotionActive: false,
        discountPercent: 30,
        effectivePriceCents: 5000000,
        discountEndsAt: "2020-01-01T00:00:00Z",
      }),
    );

    expect(card.price).toContain("50");
    expect(card.priceCents).toBe(5000000);
    expect(card.originalPriceLabel).toBeNull();
    expect(card.discountPercent).toBeNull();
    expect(card.discountEndsHint).toBeNull();
  });
});
